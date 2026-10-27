using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Web.Models;

public class NfseEditModel
{
    public string TakerName { get; set; } = string.Empty;
    public string? TakerCountry { get; set; }
    public string? TakerCountryCode { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? ActivityCode { get; set; }
    public string? ServiceListItem { get; set; }
    public string? NbsCode { get; set; }
    public string? TaxSituation { get; set; }
    public decimal IssRate { get; set; }
    public decimal AmountBrl { get; set; }
    public decimal IrAmount { get; set; }
    public decimal InssAmount { get; set; }
    public decimal CsllAmount { get; set; }
    public decimal PisAmount { get; set; }
    public decimal CofinsAmount { get; set; }
    public decimal IssWithheldAmount { get; set; }
    public string? TakerTaxId { get; set; }
    public MissingTaxIdReason? TakerMissingTaxIdReason { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal AmountForeign { get; set; }
    public ForeignServiceMode ServiceMode { get; set; } = ForeignServiceMode.CrossBorder;
    public bool RelatedToTaker { get; set; }
    public string? ResultCountryCode { get; set; }

    public static NfseEditModel FromNfse(Nfse nfse) => new()
    {
        TakerName = nfse.TakerName,
        TakerCountry = nfse.TakerCountry,
        TakerCountryCode = nfse.TakerCountryCode,
        ServiceDate = nfse.ServiceDate,
        Description = nfse.Description,
        Notes = nfse.Notes,
        ActivityCode = nfse.ActivityCode,
        ServiceListItem = nfse.ServiceListItem,
        NbsCode = nfse.NbsCode,
        TaxSituation = nfse.TaxSituation,
        IssRate = nfse.IssRate,
        AmountBrl = nfse.AmountBrl,
        IrAmount = nfse.IrAmount,
        InssAmount = nfse.InssAmount,
        CsllAmount = nfse.CsllAmount,
        PisAmount = nfse.PisAmount,
        CofinsAmount = nfse.CofinsAmount,
        IssWithheldAmount = nfse.IssWithheldAmount,
        TakerTaxId = nfse.TakerTaxId,
        TakerMissingTaxIdReason = nfse.TakerMissingTaxIdReason,
        CurrencyCode = nfse.CurrencyCode,
        AmountForeign = nfse.AmountForeign,
        ServiceMode = nfse.ServiceMode,
        RelatedToTaker = nfse.RelatedToTaker,
        ResultCountryCode = nfse.ResultCountryCode
    };
}

public class NfseActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? NfseId { get; init; }
    public List<string> Warnings { get; init; } = [];

    public static NfseActionResult Ok(string message, int? nfseId = null, List<string>? warnings = null) =>
        new() { Success = true, Message = message, NfseId = nfseId, Warnings = warnings ?? [] };

    public static NfseActionResult Fail(string message, int? nfseId = null) =>
        new() { Success = false, Message = message, NfseId = nfseId };
}

/// <summary>Result of reading one XML file before it is imported.</summary>
public class NfseImportPreview
{
    public required string FileName { get; init; }
    public byte[] Content { get; init; } = [];
    public NfseXmlData? Data { get; init; }
    public string? Error { get; init; }
    public Nfse? AlreadyImported { get; init; }
    public int? SuggestedInvoiceId { get; init; }

    public bool CanImport => Data is not null && Error is null && AlreadyImported is null;
}

public record InvoiceOption(int Id, string InvoiceNumber, string ClientName, int ServiceYear, int ServiceMonth, decimal Total, string Currency, InvoiceStatus Status)
{
    public string Label =>
        $"{InvoiceNumber} — {ClientName} — {new DateOnly(ServiceYear, ServiceMonth, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)} — {Total.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";
}
