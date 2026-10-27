using System.ComponentModel.DataAnnotations;
using InvoiceApp.Web.Data.Entities;

namespace InvoiceApp.Web.Models;

public class InvoiceReportModel
{
    public required CompanySettings Company { get; init; }
    public required Invoice Invoice { get; init; }

    public string FormattedInvoiceDate => Invoice.InvoiceDate.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
    public string FormattedDueDate => Invoice.DueDate.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
    public string FormattedSubtotal => FormatAmount(Invoice.Subtotal);
    public string FormattedTax => FormatAmount(Invoice.Tax);
    public string FormattedTotal => FormatAmount(Invoice.Total);

    public IReadOnlyList<InvoiceItem> Items => Invoice.Items.OrderBy(x => x.Position).ToList();

    public static string FormatAmount(decimal amount) =>
        amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    public static string FormatQuantity(InvoiceItem item)
    {
        var quantity = item.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(item.Unit) ? quantity : $"{quantity} {item.Unit}";
    }
}

public class InvoiceFilterModel
{
    public int? ClientId { get; set; }
    public int? IssueYear { get; set; }
    public int? IssueMonth { get; set; }
    public int? ServiceYear { get; set; }
    public int? ServiceMonth { get; set; }
    public InvoiceStatus? Status { get; set; }
    public string? Search { get; set; }
}

public class GenerateInvoicesRequest
{
    public int IssueYear { get; set; }
    public int IssueMonth { get; set; }
    public bool ExportPdf { get; set; } = true;
}

public class GenerateInvoicesResult
{
    public int CreatedCount { get; set; }
    public List<string> InvoiceNumbers { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

public class InvoiceEditModel
{
    [Required]
    public DateOnly InvoiceDate { get; set; }

    [Required]
    public DateOnly DueDate { get; set; }

    [Required(ErrorMessage = "Currency is required.")]
    [MaxLength(10)]
    public string Currency { get; set; } = string.Empty;

    public string CurrencyLabel { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Tax cannot be negative.")]
    public decimal Tax { get; set; }

    public string WisePaymentLink { get; set; } = string.Empty;

    public List<InvoiceItemEditModel> Items { get; set; } = [];

    public decimal Subtotal => Items.Sum(x => x.Amount);

    public decimal Total => Subtotal + Tax;

    public static InvoiceEditModel FromInvoice(Invoice invoice) => new()
    {
        InvoiceDate = invoice.InvoiceDate,
        DueDate = invoice.DueDate,
        Currency = invoice.Currency,
        CurrencyLabel = invoice.CurrencyLabel,
        Tax = invoice.Tax,
        WisePaymentLink = invoice.WisePaymentLink,
        Items = invoice.Items
            .OrderBy(x => x.Position)
            .Select(x => new InvoiceItemEditModel
            {
                Description = x.Description,
                Quantity = x.Quantity,
                Unit = x.Unit,
                UnitPrice = x.UnitPrice
            })
            .ToList()
    };
}

public class InvoiceItemEditModel
{
    [Required(ErrorMessage = "Item description is required.")]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.0001", "999999999", ErrorMessage = "Item quantity must be greater than zero.")]
    public decimal Quantity { get; set; } = 1;

    [MaxLength(30)]
    public string Unit { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Item unit price cannot be negative.")]
    public decimal UnitPrice { get; set; }

    public decimal Amount => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
}

public class InvoiceActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Warning { get; init; }

    public static InvoiceActionResult Ok(string message, string? warning = null) =>
        new() { Success = true, Message = message, Warning = warning };

    public static InvoiceActionResult Fail(string message) =>
        new() { Success = false, Message = message };
}
