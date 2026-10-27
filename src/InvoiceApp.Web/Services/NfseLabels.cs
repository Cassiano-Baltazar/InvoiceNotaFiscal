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

    /// <summary>BRL received per unit of the foreign currency, when both amounts are known.</summary>
    public static decimal? ExchangeRate(Nfse nfse)
    {
        var foreign = nfse.AmountForeign > 0 ? nfse.AmountForeign : nfse.Invoice?.Total ?? 0;
        return nfse.AmountBrl > 0 && foreign > 0 ? Math.Round(nfse.AmountBrl / foreign, 4) : null;
    }

    public static string TaxIdReasonLabel(MissingTaxIdReason reason) => reason switch
    {
        MissingTaxIdReason.NotInformed => "Not informed",
        MissingTaxIdReason.Exempt => "Exempt from tax id",
        MissingTaxIdReason.NotRequired => "Tax id not required",
        _ => reason.ToString()
    };

    public static string ServiceModeLabel(ForeignServiceMode mode) => mode switch
    {
        ForeignServiceMode.Unknown => "Unknown",
        ForeignServiceMode.CrossBorder => "Cross-border (provided from Brazil)",
        ForeignServiceMode.ConsumptionInBrazil => "Consumed in Brazil",
        ForeignServiceMode.CommercialPresenceAbroad => "Commercial presence abroad",
        ForeignServiceMode.TemporaryPresenceOfPersons => "Temporary presence of persons",
        _ => mode.ToString()
    };
}
