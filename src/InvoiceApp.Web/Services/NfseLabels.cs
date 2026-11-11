using System.Globalization;
using InvoiceApp.Web.Data.Entities;
using static InvoiceApp.Web.Services.Ui;

namespace InvoiceApp.Web.Services;

public static class NfseLabels
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Status(NfseStatus status) => status switch
    {
        NfseStatus.Draft => T("Draft"),
        NfseStatus.Issued => T("Issued"),
        NfseStatus.Replaced => T("Replaced"),
        NfseStatus.Cancelled => T("Cancelled"),
        _ => status.ToString()
    };

    public static string Source(NfseSource source) => source switch
    {
        NfseSource.Manual => T("Manual"),
        NfseSource.Imported => T("Imported"),
        NfseSource.NationalApi => "Emissor Nacional",
        _ => source.ToString()
    };

    public static string Environment(NfseEnvironment environment) => environment switch
    {
        NfseEnvironment.Production => T("Production"),
        NfseEnvironment.RestrictedProduction => T("Restricted production (tests, no fiscal value)"),
        _ => environment.ToString()
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
        MissingTaxIdReason.NotInformed => T("Not informed"),
        MissingTaxIdReason.Exempt => T("Exempt from tax id"),
        MissingTaxIdReason.NotRequired => T("Tax id not required"),
        _ => reason.ToString()
    };

    public static string ServiceModeLabel(ForeignServiceMode mode) => mode switch
    {
        ForeignServiceMode.Unknown => T("Unknown"),
        ForeignServiceMode.CrossBorder => T("Cross-border (provided from Brazil)"),
        ForeignServiceMode.ConsumptionInBrazil => T("Consumed in Brazil"),
        ForeignServiceMode.CommercialPresenceAbroad => T("Commercial presence abroad"),
        ForeignServiceMode.TemporaryPresenceOfPersons => T("Temporary presence of persons"),
        _ => mode.ToString()
    };
}
