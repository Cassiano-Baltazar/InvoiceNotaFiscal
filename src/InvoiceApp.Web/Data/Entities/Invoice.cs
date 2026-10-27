namespace InvoiceApp.Web.Data.Entities;

public class Invoice
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public string ClientName { get; set; } = string.Empty;
    public string? ClientAddressLine1 { get; set; }
    public string? ClientAddressLine2 { get; set; }
    public string? ClientAddressLine3 { get; set; }
    public string? ClientCity { get; set; }
    public string? ClientRegion { get; set; }
    public string? ClientPostalCode { get; set; }
    public string ClientCountry { get; set; } = string.Empty;

    public int ServiceYear { get; set; }
    public int ServiceMonth { get; set; }
    public int IssueYear { get; set; }
    public int IssueMonth { get; set; }

    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string CurrencyLabel { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string WisePaymentLink { get; set; } = string.Empty;
    public string ServiceDescription { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Generated;
    public string? PdfPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<InvoiceItem> Items { get; set; } = [];
}
