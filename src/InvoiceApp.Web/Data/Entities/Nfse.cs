namespace InvoiceApp.Web.Data.Entities;

/// <summary>
/// Brazilian service invoice (NFS-e) linked to an invoice.
/// The app does not issue NFS-e yet: a note is either a draft prepared here
/// or imported from the XML downloaded from the city hall portal.
/// </summary>
public class Nfse
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public NfseStatus Status { get; set; } = NfseStatus.Draft;
    public NfseSource Source { get; set; } = NfseSource.Manual;

    // Identification (filled when issued)
    public string? Number { get; set; }
    public string? Series { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public string? VerificationCode { get; set; }
    public string? NationalAccessKey { get; set; }
    public string? Link { get; set; }
    public int? MunicipalStatusCode { get; set; }
    public string? MunicipalStatusDescription { get; set; }
    public string? ProviderCnpj { get; set; }

    // Service taker (the foreign client)
    public string TakerName { get; set; } = string.Empty;
    public string? TakerCountry { get; set; }
    public string? TakerCountryCode { get; set; }

    // Service
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? ActivityCode { get; set; }
    public string? ServiceListItem { get; set; }
    public string? NbsCode { get; set; }
    public string? TaxSituation { get; set; }
    public decimal IssRate { get; set; }

    // Amounts in BRL
    public decimal AmountBrl { get; set; }
    public decimal Discount { get; set; }
    public decimal IrAmount { get; set; }
    public decimal InssAmount { get; set; }
    public decimal CsllAmount { get; set; }
    public decimal PisAmount { get; set; }
    public decimal CofinsAmount { get; set; }
    public decimal IssWithheldAmount { get; set; }

    // Original file when imported
    public string? FileName { get; set; }
    public string? XmlContent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public decimal TotalWithheld => IrAmount + InssAmount + CsllAmount + PisAmount + CofinsAmount + IssWithheldAmount;
}

public enum NfseStatus
{
    Draft = 0,
    Issued = 1,
    Replaced = 2,
    Cancelled = 3
}

public enum NfseSource
{
    Manual = 0,
    Imported = 1
}
