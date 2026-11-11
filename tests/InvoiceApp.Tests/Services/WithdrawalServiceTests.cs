using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class WithdrawalServiceTests : ServiceTestBase
{
    private WithdrawalService CreateService()
    {
        var context = NewContext();
        return new WithdrawalService(context, new CompanySettingsService(context));
    }

    private int invoiceId;
    private int noteNumber;

    private void SeedCompany(DateOnly? start = null) => Database.SeedCompany(x =>
    {
        x.CompanyStartDate = start ?? new DateOnly(2026, 8, 25);
        x.AccountantMonthlyFee = 257m;
        x.ProLaboreRate = 28m;
    });

    private void SeedNote(int year, int month, decimal amount, NfseStatus status = NfseStatus.Issued)
    {
        if (invoiceId == 0)
            invoiceId = Database.SeedInvoice(Database.SeedClient().Id).Id;

        using var db = Database.CreateContext();
        db.Nfses.Add(new Nfse
        {
            InvoiceId = invoiceId,
            Status = status,
            Number = (++noteNumber).ToString(),
            Series = "1",
            IssuedAt = new DateTime(year, month, 4, 10, 0, 0),
            TakerName = "Client",
            Description = "Services",
            AmountBrl = amount
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task November_2026_example()
    {
        SeedCompany();
        SeedNote(2026, 9, 15_000m);
        SeedNote(2026, 10, 15_000m);
        SeedNote(2026, 11, 15_487.82m);
        SeedNote(2026, 11, 5_000m, NfseStatus.Cancelled);

        var result = await CreateService().GetMonthAsync(2026, 11);

        Assert.Single(result.Notes);
        Assert.Equal(15_487.82m, result.Revenue);
        // August (start, no revenue), September and October: average 10.000 × 12.
        Assert.Equal(120_000m, result.Rbt12);
        Assert.True(result.Rbt12Annualized);
        Assert.Equal(1, result.Bracket.Number);
        Assert.Equal(473.00m, result.Das);
        Assert.Equal(4_336.59m, result.ProLaboreGross);
        Assert.Equal(477.02m, result.Inss);
        Assert.Equal(0m, result.Irrf);
        Assert.Equal(3_859.57m, result.ProLaboreNet);
        Assert.Equal(10_421.23m, result.Profit);
        Assert.Equal(14_280.80m, result.TotalWithdrawal);
        Assert.Equal(950.02m, result.TaxReserve);
        Assert.Equal(new DateOnly(2026, 12, 20), result.DueDate);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Guide_amount_replaces_the_estimate_and_other_revenue_counts()
    {
        SeedCompany();
        SeedNote(2026, 11, 10_000m);

        await CreateService().SaveMonthAsync(2026, 11, 2_000m, 400m);
        var result = await CreateService().GetMonthAsync(2026, 11);

        Assert.Equal(12_000m, result.Revenue);
        Assert.Equal(400m, result.Das);
        Assert.Equal(400m, result.DasGuideAmount);
        Assert.Equal(3_360m, result.ProLaboreGross);

        await CreateService().SaveMonthAsync(2026, 11, 0m, null);
        result = await CreateService().GetMonthAsync(2026, 11);
        Assert.Null(result.DasGuideAmount);
        Assert.Equal(result.EstimatedDas, result.Das);
        Assert.Equal(10_000m, result.Revenue);
    }

    [Fact]
    public async Task Other_revenue_of_earlier_months_counts_for_the_rbt12()
    {
        SeedCompany(new DateOnly(2025, 1, 10));
        for (var month = 1; month <= 12; month++)
            await CreateService().SaveMonthAsync(2025, month, 20_000m, null);
        SeedNote(2026, 1, 22_000m);

        var result = await CreateService().GetMonthAsync(2026, 1);

        Assert.Equal(240_000m, result.Rbt12);
        Assert.False(result.Rbt12Annualized);
        Assert.Equal(2, result.Bracket.Number);
    }

    [Fact]
    public async Task Without_start_date_the_first_month_with_revenue_is_the_start()
    {
        Database.SeedCompany(x => x.ProLaboreRate = 28m);
        SeedNote(2026, 10, 12_000m);
        SeedNote(2026, 11, 15_000m);

        var result = await CreateService().GetMonthAsync(2026, 11);

        Assert.Equal(144_000m, result.Rbt12);
        Assert.Contains(result.Warnings, x => x.Contains("start date"));
        Assert.Contains(result.Warnings, x => x.Contains("accountant"));
    }

    [Fact]
    public async Task Warns_when_pro_labore_is_below_fator_r()
    {
        Database.SeedCompany(x =>
        {
            x.CompanyStartDate = new DateOnly(2026, 8, 25);
            x.AccountantMonthlyFee = 257m;
            x.ProLaboreRate = 20m;
        });
        SeedNote(2026, 11, 10_000m);

        var result = await CreateService().GetMonthAsync(2026, 11);

        Assert.Contains(result.Warnings, x => x.Contains("Anexo V"));
    }
}
