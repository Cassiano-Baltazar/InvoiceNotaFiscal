using System.Globalization;

namespace CalculoProLabore;

/// <summary>Reads and writes amounts in the Brazilian format (21.489,08).</summary>
internal static class Money
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Parses "21.489,08", "21489,08", "R$ 21.489,08" and also "21489.08" (dot as the decimal
    /// separator when there is no comma and one or two digits after the last dot).
    /// </summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = text.Replace("R$", "").Replace("%", "").Replace(" ", "").Trim();
        if (!s.Contains(',') && s.Count(c => c == '.') == 1 && s.Length - s.LastIndexOf('.') - 1 is 1 or 2)
            s = s.Replace('.', ',');
        return decimal.TryParse(s, NumberStyles.Number, PtBr, out var value) ? value : null;
    }

    public static string Format(decimal value) => value.ToString("N2", PtBr);

    public static string Brl(decimal value) => "R$ " + Format(value);

    public static string Percent(decimal fraction, int decimals = 2) =>
        (fraction * 100m).ToString("N" + decimals, PtBr) + "%";
}
