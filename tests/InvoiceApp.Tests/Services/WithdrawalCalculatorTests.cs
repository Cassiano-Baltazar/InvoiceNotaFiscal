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
    }

    [Fact]
    public void Rbt12_after_months_without_revenue_is_zero()
    {
        // PGDAS-D 09/2026: second month, August without revenue: RBT12p 0,00 and bracket 1.
        Assert.Equal((0m, true), WithdrawalCalculator.Rbt12([0m], 21_489.08m));
        Assert.Equal(656.28m, Math.Round(21_489.08m * WithdrawalCalculator.ExportDasRate(0m), 2));
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

    [Fact]
    public void Anexo_v_export_rate_matches_the_october_2026_declaration()
    {
        // PGDAS-D 10/2026: R$ 43.244,13 in Anexo V, bracket 1, debt R$ 4.614,91 (IRPJ, CSLL and CPP only).
        var rate = WithdrawalCalculator.ExportDasRate(128_934.48m, anexoV: true);

        Assert.Equal(4_614.91m, Math.Round(43_244.13m * rate, 2));
        Assert.Equal("V", WithdrawalCalculator.BracketFor(128_934.48m, anexoV: true).Anexo);
    }

    [Fact]
    public void Anexo_iii_export_rate_matches_the_october_2026_declaration()
    {
        // PGDAS-D 10/2026: R$ 21.755,05 in Anexo III, debt R$ 664,40.
        Assert.Equal(664.40m, Math.Round(21_755.05m * WithdrawalCalculator.ExportDasRate(128_934.48m), 2));
    }

    [Theory]
    [InlineData(0, 21_489.08, 0, 21_755.05, false, 0.01)]     // no pró-labore declared: minimum 0,01
    [InlineData(6_017, 21_489.08, 0, 21_755.05, false, 0.28)] // months before decide
    [InlineData(0, 0, 4_336.59, 15_487.82, true, 0.28)]       // first month: the month itself
    public void Fator_r_is_payroll_over_revenue(decimal previousPayroll, decimal previousRevenue, decimal payroll, decimal revenue, bool firstMonth, decimal expected)
    {
        Assert.Equal(expected, WithdrawalCalculator.FatorR(previousPayroll, previousRevenue, payroll, revenue, firstMonth));
    }

    [Fact]
    public void Fator_r_does_not_apply_without_revenue_before()
    {
        // PGDAS-D 09/2026: "Fator r = Não se aplica", taxed in Anexo III.
        Assert.Null(WithdrawalCalculator.FatorR(0m, 0m, 0m, 21_489.08m, firstMonth: false));
    }

    [Fact]
    public void Summarize_september_2026_matches_the_pgdas()
    {
        // PGDAS-D 09/2026: revenue 21.489,08, RBT12 0, Anexo III bracket 1, DAS 656,28.
        var month = WithdrawalCalculator.Summarize(21_489.08m, rbt12: 0m, fatorR: null, proLaboreRate: 28m, accountantFee: 257m);

        Assert.Equal("III", month.Bracket.Anexo);
        Assert.Equal(1, month.Bracket.Number);
        Assert.Equal(656.28m, month.Das);
        Assert.Equal(6_016.94m, month.ProLaboreGross);
        Assert.Equal(661.86m, month.Inss);
        Assert.Equal(WithdrawalCalculator.Irrf(6_016.94m, 661.86m), month.Irrf);
        Assert.Equal(21_489.08m - 656.28m - 257m - 6_016.94m, month.Profit);
        Assert.Equal(month.ProLaboreNet + month.Profit, month.TotalWithdrawal);
        Assert.Equal(656.28m + month.Inss + month.Irrf, month.TaxReserve);
    }

    [Fact]
    public void Summarize_without_rbt12_annualizes_the_month()
    {
        var month = WithdrawalCalculator.Summarize(10_000m, rbt12: null, fatorR: null, proLaboreRate: 28m, accountantFee: 0m);

        Assert.True(month.Rbt12FromRevenue);
        Assert.Equal(120_000m, month.Rbt12);
        Assert.Equal(305.40m, month.Das);
    }

    [Fact]
    public void Summarize_uses_anexo_v_when_fator_r_is_below_28_percent()
    {
        var month = WithdrawalCalculator.Summarize(10_000m, rbt12: 120_000m, fatorR: 0.10m, proLaboreRate: 28m, accountantFee: 0m);

        Assert.True(month.AnexoV);
        Assert.Equal(Math.Round(10_000m * 0.155m * (1 - 0.3115m), 2), month.Das);
    }

    [Fact]
    public void Summarize_services_in_brazil_pay_the_whole_rate()
    {
        var month = WithdrawalCalculator.Summarize(10_000m, rbt12: 120_000m, fatorR: null, proLaboreRate: 28m, accountantFee: 0m, export: false);

        Assert.Equal(600m, month.Das);
    }

    [Fact]
    public void Summarize_never_has_a_negative_withdrawal_from_profit()
    {
        var month = WithdrawalCalculator.Summarize(300m, rbt12: null, fatorR: null, proLaboreRate: 28m, accountantFee: 257m);

        Assert.True(month.Profit < 0);
        Assert.Equal(month.ProLaboreNet, month.TotalWithdrawal);
    }
}
