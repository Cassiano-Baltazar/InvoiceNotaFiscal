using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class WithdrawalCalculatorTests
{
    [Theory]
    // Bracket 1: 6% without ISS, PIS and COFINS (49,10% of the DAS) is the 3,05% the accountant charges.
    [InlineData(120_000, 0.030540)]
    // Bracket 2 at R$ 22 mil a month: (264.000 × 11,2% − 9.360) / 264.000 × 50,9%.
    [InlineData(264_000, 0.038962)]
    public void Export_das_rate_removes_iss_pis_and_cofins(decimal rbt12, decimal expected)
    {
        Assert.Equal(expected, Math.Round(WithdrawalCalculator.ExportDasRate(rbt12), 6));
    }

    [Fact]
    public void Bracket_follows_the_revenue_of_the_last_12_months()
    {
        Assert.Equal(1, WithdrawalCalculator.BracketFor(180_000m).Number);
        Assert.Equal(2, WithdrawalCalculator.BracketFor(180_000.01m).Number);
        Assert.Equal(6, WithdrawalCalculator.BracketFor(5_000_000m).Number);
    }

    [Fact]
    public void Rbt12_in_the_first_month_is_the_month_times_12()
    {
        Assert.Equal((180_000m, true), WithdrawalCalculator.Rbt12([], 15_000m));
        Assert.Equal((180_000m, true), WithdrawalCalculator.Rbt12([0m], 15_000m));
    }

    [Fact]
    public void Rbt12_before_12_months_is_the_average_since_the_start_times_12()
    {
        Assert.Equal((120_000m, true), WithdrawalCalculator.Rbt12([0m, 15_000m, 15_000m], 99m));
    }

    [Fact]
    public void Rbt12_after_12_months_is_the_sum()
    {
        Assert.Equal((240_000m, false), WithdrawalCalculator.Rbt12(Enumerable.Repeat(20_000m, 12).ToList(), 99m));
    }

    [Theory]
    [InlineData(4_336.59, 477.02)]
    [InlineData(6_160, 677.60)]
    [InlineData(10_000, 932.31)] // capped at the 2026 ceiling
    public void Inss_is_11_percent_up_to_the_ceiling(decimal proLabore, decimal expected)
    {
        Assert.Equal(expected, WithdrawalCalculator.Inss(proLabore));
    }

    [Theory]
    [InlineData(4_336.59, 477.02, 0)]        // up to R$ 5.000 there is no IRRF in 2026
    [InlineData(6_160, 677.60, 440.48)]      // partial reduction (Lei 15.270/2025)
    [InlineData(10_000, 932.31, 1_584.88)]   // no reduction above R$ 7.350
    public void Irrf_uses_the_2026_table_and_reduction(decimal proLabore, decimal inss, decimal expected)
    {
        Assert.Equal(expected, WithdrawalCalculator.Irrf(proLabore, inss));
    }
}
