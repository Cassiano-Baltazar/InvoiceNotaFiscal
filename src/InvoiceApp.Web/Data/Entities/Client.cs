namespace InvoiceApp.Web.Data.Entities;

public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AddressLine3 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string CurrencyLabel { get; set; } = string.Empty;
    /// <summary>Amount of the monthly invoice. For a freelance client: optional usual price of a job, used to fill a new invoice.</summary>
    public decimal MonthlyAmount { get; set; }
    public string WisePaymentLink { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Data required by the NFS-e for a foreign taker
    public string? CountryCode { get; set; }
    public string? TaxId { get; set; }
    public MissingTaxIdReason? MissingTaxIdReason { get; set; }

    /// <summary>Monthly clients go into "Generate Month"; freelance clients are billed per job by hand.</summary>
    public ClientBillingType BillingType { get; set; } = ClientBillingType.Monthly;

    public ICollection<Invoice> Invoices { get; set; } = [];
}
