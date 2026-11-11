using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using static InvoiceApp.Web.Services.Ui;

namespace InvoiceApp.Web.Services;

/// <summary>How much of a month's revenue the partner can take out, and what to keep for taxes.</summary>
public sealed record MonthlyWithdrawal
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required IReadOnlyList<Nfse> Notes { get; init; }
    public required decimal NotesRevenue { get; init; }
    public required decimal OtherRevenue { get; init; }
    public decimal Revenue => NotesRevenue + OtherRevenue;

    // DAS
    public required decimal Rbt12 { get; init; }
    public required bool Rbt12Annualized { get; init; }
    public required WithdrawalCalculator.SimplesBracket Bracket { get; init; }
    public required decimal DasRate { get; init; }
    public required decimal EstimatedDas { get; init; }
    public required decimal? DasGuideAmount { get; init; }
    public decimal Das => DasGuideAmount ?? EstimatedDas;

    // Pró-labore (INSS and IRRF are paid in one guide)
    public required decimal ProLaboreRate { get; init; }
    public required decimal ProLaboreGross { get; init; }
    public required decimal Inss { get; init; }
    public required decimal Irrf { get; init; }
    public decimal PayrollTaxes => Inss + Irrf;
    public decimal ProLaboreNet => ProLaboreGross - PayrollTaxes;

    public required decimal AccountantFee { get; init; }

    /// <summary>What is left for the distribution of profits (exempt from income tax).</summary>
    public decimal Profit => Revenue - Das - AccountantFee - ProLaboreGross;
    public decimal TotalWithdrawal => ProLaboreNet + Math.Max(0m, Profit);
    public decimal TaxReserve => Das + PayrollTaxes;

    /// <summary>DAS and the INSS/IRRF guide are due on the 20th of the next month.</summary>
    public DateOnly DueDate => new DateOnly(Year, Month, 20).AddMonths(1);

    public required IReadOnlyList<string> Warnings { get; init; }
}

public interface IWithdrawalService
{
    Task<MonthlyWithdrawal> GetMonthAsync(int year, int month, CancellationToken cancellationToken = default);
    Task SaveMonthAsync(int year, int month, decimal otherRevenue, decimal? dasGuideAmount, CancellationToken cancellationToken = default);
}

public class WithdrawalService(AppDbContext db, ICompanySettingsService settingsService) : IWithdrawalService
{
    public async Task<MonthlyWithdrawal> GetMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        var monthStart = new DateOnly(year, month, 1);
        var windowStart = monthStart.AddMonths(-12);

        var issued = await db.Nfses
            .AsNoTracking()
            .Include(x => x.Invoice)
            .Where(x => x.Status == NfseStatus.Issued && x.IssuedAt != null)
            .Where(x => x.IssuedAt >= windowStart.ToDateTime(TimeOnly.MinValue)
                        && x.IssuedAt < monthStart.AddMonths(1).ToDateTime(TimeOnly.MinValue))
            .OrderBy(x => x.IssuedAt)
            .ToListAsync(cancellationToken);

        var records = await db.MonthlyTaxRecords
            .AsNoTracking()
            .Where(x => (x.Year * 12 + x.Month) >= windowStart.Year * 12 + windowStart.Month
                        && (x.Year * 12 + x.Month) <= year * 12 + month)
            .ToListAsync(cancellationToken);

        decimal NotesIn(DateOnly m) => issued
            .Where(x => x.IssuedAt!.Value.Year == m.Year && x.IssuedAt.Value.Month == m.Month)
            .Sum(x => x.AmountBrl - x.Discount);
        decimal OtherIn(DateOnly m) => records.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month)?.OtherRevenueBrl ?? 0m;
        decimal RevenueIn(DateOnly m) => NotesIn(m) + OtherIn(m);

        var record = records.FirstOrDefault(x => x.Year == year && x.Month == month);
        var notes = issued.Where(x => x.IssuedAt!.Value.Year == year && x.IssuedAt.Value.Month == month).ToList();
        var notesRevenue = NotesIn(monthStart);
        var otherRevenue = record?.OtherRevenueBrl ?? 0m;
        var revenue = notesRevenue + otherRevenue;

        // Months before this one that count for the RBT12: the last 12, but not before the company started.
        // Without a start date, the first month with revenue is taken as the start.
        var start = settings.CompanyStartDate is DateOnly startDate
            ? new DateOnly(startDate.Year, startDate.Month, 1)
            : await FirstMonthWithRevenueAsync(cancellationToken) ?? monthStart;
        var firstCounted = start > windowStart ? start : windowStart;
        var previous = new List<decimal>();
        for (var m = firstCounted; m < monthStart; m = m.AddMonths(1))
            previous.Add(RevenueIn(m));

        var (rbt12, annualized) = WithdrawalCalculator.Rbt12(previous, revenue);
        var bracket = WithdrawalCalculator.BracketFor(rbt12);
        var dasRate = WithdrawalCalculator.ExportDasRate(rbt12);
        var estimatedDas = Math.Round(revenue * dasRate, 2);

        var proLaboreGross = Math.Round(revenue * settings.ProLaboreRate / 100m, 2);
        var inss = WithdrawalCalculator.Inss(proLaboreGross);
        var irrf = WithdrawalCalculator.Irrf(proLaboreGross, inss);

        var warnings = new List<string>();
        if (settings.ProLaboreRate < 28m)
            warnings.Add(T("The pró-labore is below 28% of the revenue: with Fator R under 28% the company moves to Anexo V and the DAS is much higher."));
        if (settings.CompanyStartDate is null)
            warnings.Add(T("Set the company start date in Settings so the revenue of the last 12 months (RBT12) is right."));
        if (settings.AccountantMonthlyFee <= 0)
            warnings.Add(T("The accountant's monthly fee is not set in Settings."));
        if (rbt12 > WithdrawalCalculator.AnexoIII[^1].UpTo)
            warnings.Add(T("The revenue of the last 12 months is above the Simples Nacional limit (R$ 4,8 million)."));

        var result = new MonthlyWithdrawal
        {
            Year = year,
            Month = month,
            Notes = notes,
            NotesRevenue = notesRevenue,
            OtherRevenue = otherRevenue,
            Rbt12 = rbt12,
            Rbt12Annualized = annualized,
            Bracket = bracket,
            DasRate = dasRate,
            EstimatedDas = estimatedDas,
            DasGuideAmount = record?.DasGuideAmount,
            ProLaboreRate = settings.ProLaboreRate,
            ProLaboreGross = proLaboreGross,
            Inss = inss,
            Irrf = irrf,
            AccountantFee = settings.AccountantMonthlyFee,
            Warnings = warnings
        };

        if (result.Profit < 0)
            warnings.Add(T("The revenue does not cover the DAS, the accountant and the pró-labore this month: there is no profit to distribute."));
        // Lei 15.270/2025: profits above R$ 50 mil a month from the same company have 10% withheld.
        if (result.Profit > 50_000m)
            warnings.Add(T("Profits above R$ 50.000 in a month have 10% of income tax withheld (Lei 15.270/2025). Talk to the accountant."));

        return result;
    }

    public async Task SaveMonthAsync(int year, int month, decimal otherRevenue, decimal? dasGuideAmount, CancellationToken cancellationToken = default)
    {
        var record = await db.MonthlyTaxRecords.FirstOrDefaultAsync(x => x.Year == year && x.Month == month, cancellationToken);
        if (record is null)
        {
            record = new MonthlyTaxRecord { Year = year, Month = month };
            db.MonthlyTaxRecords.Add(record);
        }

        record.OtherRevenueBrl = Math.Max(0m, otherRevenue);
        record.DasGuideAmount = dasGuideAmount is > 0 ? dasGuideAmount : null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DateOnly?> FirstMonthWithRevenueAsync(CancellationToken cancellationToken)
    {
        var firstNote = await db.Nfses
            .Where(x => x.Status == NfseStatus.Issued && x.IssuedAt != null)
            .MinAsync(x => x.IssuedAt, cancellationToken);
        // SQLite cannot compare decimals, and there is one record per month at most.
        var firstRecord = (await db.MonthlyTaxRecords.AsNoTracking().ToListAsync(cancellationToken))
            .Where(x => x.OtherRevenueBrl > 0)
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .FirstOrDefault();

        DateOnly? first = firstNote is DateTime d ? new DateOnly(d.Year, d.Month, 1) : null;
        if (firstRecord is not null)
        {
            var recordMonth = new DateOnly(firstRecord.Year, firstRecord.Month, 1);
            if (first is null || recordMonth < first)
                first = recordMonth;
        }
        return first;
    }
}
