using System.Globalization;
using InvoiceApp.Web.Data.Entities;

namespace InvoiceApp.Web.Services;

public static class NfseLabels
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Status(NfseStatus status) => status switch
    {
        NfseStatus.Draft => "Draft",
        NfseStatus.Issued => "Issued",
        NfseStatus.Replaced => "Replaced",
        NfseStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string Brl(decimal amount) => "R$ " + amount.ToString("N2", PtBr);

    public static string Number(Nfse nfse) =>
        string.IsNullOrWhiteSpace(nfse.Number) ? "—" : $"{nfse.Number}{(string.IsNullOrWhiteSpace(nfse.Series) ? "" : "/" + nfse.Series)}";

    /// <summary>BRL received per unit of the invoice currency, when both amounts are known.</summary>
    public static decimal? ExchangeRate(Nfse nfse) =>
        nfse.AmountBrl > 0 && nfse.Invoice?.Total > 0 ? Math.Round(nfse.AmountBrl / nfse.Invoice.Total, 4) : null;
}
