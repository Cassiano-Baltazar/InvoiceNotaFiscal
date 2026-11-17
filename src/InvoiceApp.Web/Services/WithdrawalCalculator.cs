namespace InvoiceApp.Web.Services;

/// <summary>
/// Tax rules used to estimate the monthly withdrawal of a Simples Nacional company that exports its
/// services and is taxed in Anexo III when its Fator R reaches 28%, or in Anexo V when it does not.
/// Values are for 2026; review them every year.
/// </summary>
public static class WithdrawalCalculator
{
    /// <summary>One Anexo III bracket (LC 123/2006, Anexo III).</summary>
    /// <param name="ExportExcludedShare">
    /// Share of the DAS that is ISS, PIS and COFINS. Exported services do not pay them,
    /// so this part is removed from the rate (LC 123/2006, art. 18, §14).
    /// </param>
    public sealed record SimplesBracket(string Anexo, int Number, decimal UpTo, decimal NominalRate, decimal Deduction, decimal ExportExcludedShare);

    public static readonly IReadOnlyList<SimplesBracket> AnexoIII =
    [
        new("III", 1, 180_000m, 6.00m, 0m, 0.4910m),       // ISS 33,50 + COFINS 12,82 + PIS 2,78
        new("III", 2, 360_000m, 11.20m, 9_360m, 0.4910m),  // ISS 32,00 + COFINS 14,05 + PIS 3,05
        new("III", 3, 720_000m, 13.50m, 17_640m, 0.4910m), // ISS 32,50 + COFINS 13,64 + PIS 2,96
        new("III", 4, 1_800_000m, 16.00m, 35_640m, 0.4910m),
        new("III", 5, 3_600_000m, 21.00m, 125_640m, 0.4910m), // approximate: ISS is capped at 5% in this bracket
        new("III", 6, 4_800_000m, 33.00m, 648_000m, 0.1950m)  // no ISS in the DAS: COFINS 16,03 + PIS 3,47
    ];

    /// <summary>Anexo V (LC 123/2006): services subject to Fator R when the payroll is under 28% of the revenue.</summary>
    public static readonly IReadOnlyList<SimplesBracket> AnexoV =
    [
        new("V", 1, 180_000m, 15.50m, 0m, 0.3115m),        // ISS 14,00 + COFINS 14,10 + PIS 3,05
        new("V", 2, 360_000m, 18.00m, 4_500m, 0.3415m),    // ISS 17,00 + COFINS 14,10 + PIS 3,05
        new("V", 3, 720_000m, 19.50m, 9_900m, 0.3715m),    // ISS 19,00 + COFINS 14,92 + PIS 3,23
        new("V", 4, 1_800_000m, 20.50m, 17_100m, 0.4015m), // ISS 21,00 + COFINS 15,74 + PIS 3,41
        new("V", 5, 3_600_000m, 23.00m, 62_100m, 0.4065m), // ISS 23,50 + COFINS 14,10 + PIS 3,05
        new("V", 6, 4_800_000m, 30.50m, 540_000m, 0.2000m) // no ISS in the DAS: COFINS 16,44 + PIS 3,56
    ];

    /// <summary>Minimum Fator R for Anexo III.</summary>
    public const decimal FatorRThreshold = 0.28m;

    /// <summary>INSS ceiling (teto) for 2026.</summary>
    public const decimal InssCeiling = 8_475.55m;

    /// <summary>INSS withheld from the partner's pró-labore (contribuinte individual).</summary>
    public const decimal InssRate = 0.11m;

    public static SimplesBracket BracketFor(decimal rbt12, bool anexoV = false)
    {
        var table = anexoV ? AnexoV : AnexoIII;
        return table.FirstOrDefault(x => rbt12 <= x.UpTo) ?? table[^1];
    }

    /// <summary>Effective DAS rate (fraction) for exported services, given the revenue of the last 12 months.</summary>
    public static decimal ExportDasRate(decimal rbt12, bool anexoV = false)
    {
        var bracket = BracketFor(rbt12, anexoV);
        var nominal = bracket.NominalRate / 100m;
        var effective = rbt12 <= 0 || bracket.Deduction == 0
            ? nominal
            : (rbt12 * nominal - bracket.Deduction) / rbt12;
        return effective * (1 - bracket.ExportExcludedShare);
    }

    /// <summary>
    /// RBT12: revenue of the 12 months before the month. While the company has less than 12 months,
    /// it is the average of the months since the start times 12, even when that is zero; only in the
    /// company's first month it is the month's own revenue times 12 (LC 123/2006, art. 18, §§ 2 and 3).
    /// The September 2026 PGDAS-D (second month, August without revenue) used RBT12 0 and bracket 1.
    /// </summary>
    /// <param name="previousMonths">Revenue of each month before the month, oldest first, at most 12, starting at the company's start month when it is more recent.</param>
    public static (decimal Rbt12, bool Annualized) Rbt12(IReadOnlyList<decimal> previousMonths, decimal currentMonth)
    {
        if (previousMonths.Count == 0)
            return (currentMonth * 12, true);
        var total = previousMonths.Sum();
        if (previousMonths.Count < 12)
            return (Math.Round(total / previousMonths.Count * 12, 2), true);
        return (total, false);
    }

    /// <summary>
    /// Fator R: payroll (pró-labore declared) over revenue in the months before the month, the same months as
    /// the RBT12; in the company's first month, the month's own payroll and revenue. It is never below 0,01
    /// (Resolução CGSN 140/2018, art. 26). Null when it does not apply: no revenue in the months before, as in
    /// the September 2026 PGDAS-D ("Fator r = Não se aplica"), which was taxed in Anexo III.
    /// </summary>
    public static decimal? FatorR(decimal previousPayroll, decimal previousRevenue, decimal currentPayroll, decimal currentRevenue, bool firstMonth)
    {
        var (payroll, revenue) = firstMonth ? (currentPayroll, currentRevenue) : (previousPayroll, previousRevenue);
        if (revenue <= 0)
            return null;
        return Math.Max(0.01m, Math.Round(payroll / revenue, 2));
    }

    public static decimal Inss(decimal proLabore) =>
        Math.Round(Math.Min(proLabore, InssCeiling) * InssRate, 2);

    /// <summary>
    /// IRRF on the pró-labore: monthly 2026 table, with the reduction of Lei 15.270/2025
    /// (no tax up to R$ 5.000, partial reduction up to R$ 7.350). No dependents.
    /// </summary>
    public static decimal Irrf(decimal proLabore, decimal inss)
    {
        if (proLabore <= 5_000m)
            return 0m;

        const decimal simplifiedDeduction = 607.20m;
        var basis = proLabore - Math.Max(inss, simplifiedDeduction);
        var tax = basis switch
        {
            <= 2_428.80m => 0m,
            <= 2_826.65m => basis * 0.075m - 182.16m,
            <= 3_751.05m => basis * 0.15m - 394.16m,
            <= 4_664.68m => basis * 0.225m - 675.49m,
            _ => basis * 0.275m - 908.73m
        };

        if (proLabore <= 7_350m)
            tax -= Math.Min(tax, 978.62m - 0.133145m * proLabore);

        return Math.Max(0m, Math.Round(tax, 2));
    }
}
