using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
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
    public required decimal FatorR { get; init; }
    public bool AnexoV => Bracket.Anexo == "V";
    public required WithdrawalCalculator.SimplesBracket Bracket { get; init; }
    public required decimal DasRate { get; init; }
    public required decimal EstimatedDas { get; init; }
    public required decimal? DasGuideAmount { get; init; }
    public decimal Das => DasGuideAmount ?? EstimatedDas;

    // Pró-labore (INSS and IRRF are paid in one guide)
    public required decimal ProLaboreRate { get; init; }
    /// <summary>Pró-labore entered for the month; when empty the gross is the % of the revenue.</summary>
    public required decimal? ProLaboreDeclared { get; init; }
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
    Task<IReadOnlyList<MonthlyWithdrawal>> GetHistoryAsync(int year, int month, CancellationToken cancellationToken = default);
    Task SaveMonthAsync(int year, int month, decimal otherRevenue, decimal? dasGuideAmount, decimal? proLaboreDeclared = null, CancellationToken cancellationToken = default);
}

public class WithdrawalService(AppDbContext db, ICompanySettingsService settingsService) : IWithdrawalService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<MonthlyWithdrawal> GetMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        var monthStart = new DateOnly(year, month, 1);
        var windowStart = monthStart.AddMonths(-12);

        // Issued notes are few (one or two a month), so they are filtered by month in memory.
        var issued = (await db.Nfses
                .AsNoTracking()
                .Include(x => x.Invoice)
                .Where(x => x.Status == NfseStatus.Issued && x.IssuedAt != null)
                .ToListAsync(cancellationToken))
            .Select(x => (Note: x, Month: RevenueMonth(x)))
            .Where(x => x.Month >= windowStart && x.Month <= monthStart)
            .OrderBy(x => x.Note.IssuedAt)
            .ToList();

        var records = await db.MonthlyTaxRecords
            .AsNoTracking()
            .Where(x => (x.Year * 12 + x.Month) >= windowStart.Year * 12 + windowStart.Month
                        && (x.Year * 12 + x.Month) <= year * 12 + month)
            .ToListAsync(cancellationToken);

        MonthlyTaxRecord? RecordOf(DateOnly m) => records.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
        decimal NotesIn(DateOnly m) => issued.Where(x => x.Month == m).Sum(x => x.Note.AmountBrl - x.Note.Discount);
        decimal RevenueIn(DateOnly m) => NotesIn(m) + (RecordOf(m)?.OtherRevenueBrl ?? 0m);
        // Without a declared amount, the pró-labore is the % set in Settings.
        decimal PayrollIn(DateOnly m) => RecordOf(m)?.ProLaboreDeclared ?? Math.Round(RevenueIn(m) * settings.ProLaboreRate / 100m, 2);

        var record = RecordOf(monthStart);
        var notes = issued.Where(x => x.Month == monthStart).Select(x => x.Note).ToList();
        var notesRevenue = NotesIn(monthStart);
        var otherRevenue = record?.OtherRevenueBrl ?? 0m;
        var revenue = notesRevenue + otherRevenue;

        // Months before this one that count for the RBT12 and the Fator R: the last 12, but not before the
        // company started. Without a start date, the first month with revenue is taken as the start.
        var start = settings.CompanyStartDate is DateOnly startDate
            ? new DateOnly(startDate.Year, startDate.Month, 1)
            : await FirstMonthWithRevenueAsync(cancellationToken) ?? monthStart;
        var firstCounted = start > windowStart ? start : windowStart;
        var previousMonths = new List<DateOnly>();
        for (var m = firstCounted; m < monthStart; m = m.AddMonths(1))
            previousMonths.Add(m);

        var (rbt12, annualized) = WithdrawalCalculator.Rbt12(previousMonths.Select(RevenueIn).ToList(), revenue);

        var proLaboreGross = PayrollIn(monthStart);
        var fatorR = WithdrawalCalculator.FatorR(
            previousMonths.Sum(PayrollIn), previousMonths.Sum(RevenueIn), proLaboreGross, revenue);
        var anexoV = fatorR < WithdrawalCalculator.FatorRThreshold;
        var bracket = WithdrawalCalculator.BracketFor(rbt12, anexoV);
        var dasRate = WithdrawalCalculator.ExportDasRate(rbt12, anexoV);
        var estimatedDas = Math.Round(revenue * dasRate, 2);

        var inss = WithdrawalCalculator.Inss(proLaboreGross);
        var irrf = WithdrawalCalculator.Irrf(proLaboreGross, inss);

        var warnings = new List<string>();
        if (anexoV && revenue > 0)
            warnings.Add(T("Fator R is {0} (below 28%): the DAS of this month is in Anexo V. Declare a pró-labore of at least 28% of the revenue to go back to Anexo III.",
                fatorR.ToString("0.00", PtBr)));
        else if (settings.ProLaboreRate < 28m && record?.ProLaboreDeclared is null)
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
            FatorR = fatorR,
            Bracket = bracket,
            DasRate = dasRate,
            EstimatedDas = estimatedDas,
            DasGuideAmount = record?.DasGuideAmount,
            ProLaboreRate = settings.ProLaboreRate,
            ProLaboreDeclared = record?.ProLaboreDeclared,
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

    public async Task<IReadOnlyList<MonthlyWithdrawal>> GetHistoryAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        var last = new DateOnly(year, month, 1);
        var first = last.AddMonths(-11);
        var start = settings.CompanyStartDate is DateOnly startDate
            ? new DateOnly(startDate.Year, startDate.Month, 1)
            : await FirstMonthWithRevenueAsync(cancellationToken) ?? last;
        if (start > first)
            first = start > last ? last : start;

        var history = new List<MonthlyWithdrawal>();
        for (var m = last; m >= first; m = m.AddMonths(-1))
            history.Add(await GetMonthAsync(m.Year, m.Month, cancellationToken));
        return history;
    }

    /// <summary>A note counts in the month it was issued (regime de competência of the PGDAS-D).</summary>
    private static DateOnly RevenueMonth(Nfse note) =>
        new(note.IssuedAt!.Value.Year, note.IssuedAt.Value.Month, 1);

    public async Task SaveMonthAsync(int year, int month, decimal otherRevenue, decimal? dasGuideAmount, decimal? proLaboreDeclared = null, CancellationToken cancellationToken = default)
    {
        var record = await db.MonthlyTaxRecords.FirstOrDefaultAsync(x => x.Year == year && x.Month == month, cancellationToken);
        if (record is null)
        {
            record = new MonthlyTaxRecord { Year = year, Month = month };
            db.MonthlyTaxRecords.Add(record);
        }

        record.OtherRevenueBrl = Math.Max(0m, otherRevenue);
        record.DasGuideAmount = dasGuideAmount is > 0 ? dasGuideAmount : null;
        record.ProLaboreDeclared = proLaboreDeclared is decimal declared ? Math.Max(0m, declared) : null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DateOnly?> FirstMonthWithRevenueAsync(CancellationToken cancellationToken)
    {
        var firstNote = (await db.Nfses
                .AsNoTracking()
                .Where(x => x.Status == NfseStatus.Issued && x.IssuedAt != null)
                .ToListAsync(cancellationToken))
            .Select(x => (DateOnly?)RevenueMonth(x))
            .Min();
        // SQLite cannot compare decimals, and there is one record per month at most.
        var firstRecord = (await db.MonthlyTaxRecords.AsNoTracking().ToListAsync(cancellationToken))
            .Where(x => x.OtherRevenueBrl > 0)
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .FirstOrDefault();

        var first = firstNote;
        if (firstRecord is not null)
        {
            var recordMonth = new DateOnly(firstRecord.Year, firstRecord.Month, 1);
            if (first is null || recordMonth < first)
                first = recordMonth;
        }
        return first;
    }
}
