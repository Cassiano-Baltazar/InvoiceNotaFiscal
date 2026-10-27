using System.Globalization;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Services;

public interface IInvoiceService
{
    Task<List<Invoice>> GetInvoicesAsync(InvoiceFilterModel filter, CancellationToken cancellationToken = default);
    Task<Invoice?> GetInvoiceAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceReportModel?> GetReportModelAsync(int id, CancellationToken cancellationToken = default);
    Task<GenerateInvoicesResult> GenerateMonthlyInvoicesAsync(GenerateInvoicesRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceActionResult> UpdateAsync(int id, InvoiceEditModel model, CancellationToken cancellationToken = default);
    Task<InvoiceActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceActionResult> CancelAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceActionResult> ReactivateAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceActionResult> MarkAsSentAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceActionResult> MarkAsPaidAsync(int id, CancellationToken cancellationToken = default);
    Task<byte[]> ExportPdfAsync(int id, CancellationToken cancellationToken = default);
    Task<string?> SavePdfToDiskAsync(int id, CancellationToken cancellationToken = default);
}

public class InvoiceService(
    AppDbContext db,
    IPdfExportService pdfExportService,
    INfseService nfseService,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    public async Task<List<Invoice>> GetInvoicesAsync(InvoiceFilterModel filter, CancellationToken cancellationToken = default)
    {
        var query = db.Invoices
            .Include(x => x.Client)
            .Include(x => x.Nfses)
            .AsQueryable();

        if (filter.ClientId is int clientId)
            query = query.Where(x => x.ClientId == clientId);

        if (filter.IssueYear is int issueYear)
            query = query.Where(x => x.IssueYear == issueYear);

        if (filter.IssueMonth is int issueMonth)
            query = query.Where(x => x.IssueMonth == issueMonth);

        if (filter.ServiceYear is int serviceYear)
            query = query.Where(x => x.ServiceYear == serviceYear);

        if (filter.ServiceMonth is int serviceMonth)
            query = query.Where(x => x.ServiceMonth == serviceMonth);

        if (filter.Status is InvoiceStatus status)
            query = query.Where(x => x.Status == status);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x =>
                x.InvoiceNumber.Contains(search) ||
                x.ClientName.Contains(search));
        }

        return await query
            .OrderByDescending(x => x.IssueYear)
            .ThenByDescending(x => x.IssueMonth)
            .ThenBy(x => x.InvoiceNumber)
            .ToListAsync(cancellationToken);
    }

    public Task<Invoice?> GetInvoiceAsync(int id, CancellationToken cancellationToken = default) =>
        db.Invoices
            .Include(x => x.Client)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<InvoiceReportModel?> GetReportModelAsync(int id, CancellationToken cancellationToken = default)
    {
        var invoice = await GetInvoiceAsync(id, cancellationToken);
        if (invoice is null)
            return null;

        var company = await db.CompanySettings.FirstAsync(cancellationToken);
        return new InvoiceReportModel { Company = company, Invoice = invoice };
    }

    public async Task<GenerateInvoicesResult> GenerateMonthlyInvoicesAsync(
        GenerateInvoicesRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new GenerateInvoicesResult();
        var company = await db.CompanySettings.FirstAsync(cancellationToken);
        var clients = await db.Clients.Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(cancellationToken);

        if (clients.Count == 0)
        {
            result.Errors.Add("No active clients found.");
            return result;
        }

        var serviceDate = new DateOnly(request.IssueYear, request.IssueMonth, 1).AddMonths(-1);
        var issueDay = Math.Min(company.IssueDay, DateTime.DaysInMonth(request.IssueYear, request.IssueMonth));
        var invoiceDate = new DateOnly(request.IssueYear, request.IssueMonth, issueDay);
        var dueDate = invoiceDate.AddDays(company.PaymentTermsDays);

        var existingClientIds = await db.Invoices
            .Where(x => x.IssueYear == request.IssueYear && x.IssueMonth == request.IssueMonth)
            .Select(x => x.ClientId)
            .ToListAsync(cancellationToken);

        // Uses the highest sequence (not the count) so numbers are never reused after a delete.
        var existingNumbers = await db.Invoices
            .Where(x => x.IssueYear == request.IssueYear && x.IssueMonth == request.IssueMonth)
            .Select(x => x.InvoiceNumber)
            .ToListAsync(cancellationToken);
        var sequence = existingNumbers.Count == 0 ? 0 : existingNumbers.Max(ExtractSequence);

        foreach (var client in clients)
        {
            if (existingClientIds.Contains(client.Id))
            {
                result.Errors.Add($"Invoice already exists for {client.Name} in {request.IssueYear}-{request.IssueMonth:D2}.");
                continue;
            }

            sequence++;
            var invoiceNumber = $"INV-{request.IssueYear}-{request.IssueMonth:D2}-{sequence:D3}";
            var serviceMonthLabel = serviceDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            var serviceDescription = company.ServiceDescriptionTemplate
                .Replace("{serviceMonth}", serviceMonthLabel, StringComparison.OrdinalIgnoreCase);

            var invoice = new Invoice
            {
                InvoiceNumber = invoiceNumber,
                ClientId = client.Id,
                ClientName = client.Name,
                ClientAddressLine1 = client.AddressLine1,
                ClientAddressLine2 = client.AddressLine2,
                ClientAddressLine3 = client.AddressLine3,
                ClientCity = client.City,
                ClientRegion = client.Region,
                ClientPostalCode = client.PostalCode,
                ClientCountry = client.Country,
                ServiceYear = serviceDate.Year,
                ServiceMonth = serviceDate.Month,
                IssueYear = request.IssueYear,
                IssueMonth = request.IssueMonth,
                InvoiceDate = invoiceDate,
                DueDate = dueDate,
                Currency = client.Currency,
                CurrencyLabel = client.CurrencyLabel,
                Subtotal = client.MonthlyAmount,
                Tax = 0,
                Total = client.MonthlyAmount,
                WisePaymentLink = client.WisePaymentLink,
                ServiceDescription = serviceDescription,
                Status = InvoiceStatus.Generated,
                CreatedAt = DateTime.UtcNow,
                Items =
                [
                    new InvoiceItem
                    {
                        Position = 1,
                        Description = serviceDescription,
                        Quantity = 1,
                        Unit = "month",
                        UnitPrice = client.MonthlyAmount,
                        Amount = client.MonthlyAmount
                    }
                ]
            };

            db.Invoices.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);

            if (request.ExportPdf)
            {
                try
                {
                    await SavePdfToDiskAsync(invoice.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to export PDF for invoice {InvoiceNumber}", invoice.InvoiceNumber);
                    result.Errors.Add($"PDF export failed for {invoice.InvoiceNumber}: {ex.Message}");
                }
            }

            result.CreatedCount++;
            result.InvoiceNumbers.Add(invoice.InvoiceNumber);
        }

        return result;
    }

    public async Task<InvoiceActionResult> UpdateAsync(int id, InvoiceEditModel model, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (invoice is null)
            return InvoiceActionResult.Fail("Invoice not found.");

        if (!InvoiceRules.CanEdit(invoice.Status))
            return InvoiceActionResult.Fail($"A {InvoiceRules.GetLabel(invoice.Status).ToLowerInvariant()} invoice cannot be edited.");

        var validationError = Validate(model);
        if (validationError is not null)
            return InvoiceActionResult.Fail(validationError);

        var wasSent = invoice.Status == InvoiceStatus.Sent;

        invoice.InvoiceDate = model.InvoiceDate;
        invoice.DueDate = model.DueDate;
        invoice.Currency = model.Currency.Trim();
        invoice.CurrencyLabel = model.CurrencyLabel.Trim();
        invoice.Tax = model.Tax;
        invoice.WisePaymentLink = model.WisePaymentLink.Trim();

        db.InvoiceItems.RemoveRange(invoice.Items);
        invoice.Items = model.Items
            .Select((item, index) => new InvoiceItem
            {
                Position = index + 1,
                Description = item.Description.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit.Trim(),
                UnitPrice = item.UnitPrice,
                Amount = item.Amount
            })
            .ToList();

        invoice.Subtotal = invoice.Items.Sum(x => x.Amount);
        invoice.Total = invoice.Subtotal + invoice.Tax;
        // Kept in sync with the first item for older code paths and searches.
        invoice.ServiceDescription = invoice.Items[0].Description;

        // An edited invoice is a new version that still has to be sent to the client.
        if (wasSent)
            invoice.Status = InvoiceStatus.Generated;

        await db.SaveChangesAsync(cancellationToken);

        var message = wasSent
            ? "Invoice updated. Status changed back to Not Sent."
            : "Invoice updated.";

        string? warning = null;
        if (!string.IsNullOrWhiteSpace(invoice.PdfPath))
        {
            try
            {
                await SavePdfToDiskAsync(id, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to regenerate PDF for invoice {InvoiceNumber}", invoice.InvoiceNumber);
                warning = $"The saved PDF could not be regenerated: {ex.Message}";
            }
        }

        return InvoiceActionResult.Ok(message, warning);
    }

    public async Task<InvoiceActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.FindAsync([id], cancellationToken);
        if (invoice is null)
            return InvoiceActionResult.Fail("Invoice not found.");

        if (!InvoiceRules.CanDelete(invoice.Status))
            return InvoiceActionResult.Fail($"A {InvoiceRules.GetLabel(invoice.Status).ToLowerInvariant()} invoice cannot be deleted.");

        if (await db.Nfses.AnyAsync(x => x.InvoiceId == id, cancellationToken))
            return InvoiceActionResult.Fail($"Invoice {invoice.InvoiceNumber} has an NFS-e linked. Delete the NFS-e first.");

        string? warning = null;
        if (!string.IsNullOrWhiteSpace(invoice.PdfPath) && File.Exists(invoice.PdfPath))
        {
            try
            {
                File.Delete(invoice.PdfPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete PDF {Path}", invoice.PdfPath);
                warning = $"The PDF file could not be deleted: {invoice.PdfPath}";
            }
        }

        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return InvoiceActionResult.Ok($"Invoice {invoice.InvoiceNumber} deleted.", warning);
    }

    public Task<InvoiceActionResult> CancelAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, InvoiceRules.CanCancel, InvoiceStatus.Cancelled, "cancelled", cancellationToken);

    public Task<InvoiceActionResult> ReactivateAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, InvoiceRules.CanReactivate, InvoiceStatus.Generated, "reactivated", cancellationToken);

    public Task<InvoiceActionResult> MarkAsSentAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, InvoiceRules.CanMarkAsSent, InvoiceStatus.Sent, "marked as sent", cancellationToken);

    public async Task<InvoiceActionResult> MarkAsPaidAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await ChangeStatusAsync(id, InvoiceRules.CanMarkAsPaid, InvoiceStatus.Paid, "marked as paid", cancellationToken);
        if (!result.Success)
            return result;

        // A paid invoice needs an NFS-e: prepare the draft (or keep the one already linked).
        var nfse = await nfseService.CreateDraftForInvoiceAsync(id, cancellationToken);
        return nfse.Success
            ? InvoiceActionResult.Ok($"{result.Message} {nfse.Message}")
            : InvoiceActionResult.Ok(result.Message, $"The NFS-e draft could not be created: {nfse.Message}");
    }

    private async Task<InvoiceActionResult> ChangeStatusAsync(
        int id,
        Func<InvoiceStatus, bool> isAllowed,
        InvoiceStatus newStatus,
        string actionLabel,
        CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.FindAsync([id], cancellationToken);
        if (invoice is null)
            return InvoiceActionResult.Fail("Invoice not found.");

        if (!isAllowed(invoice.Status))
            return InvoiceActionResult.Fail(
                $"A {InvoiceRules.GetLabel(invoice.Status).ToLowerInvariant()} invoice cannot be {actionLabel}.");

        invoice.Status = newStatus;
        await db.SaveChangesAsync(cancellationToken);
        return InvoiceActionResult.Ok($"Invoice {invoice.InvoiceNumber} {actionLabel}.");
    }

    private static string? Validate(InvoiceEditModel model)
    {
        if (model.DueDate < model.InvoiceDate)
            return "Due date cannot be before the invoice date.";
        if (model.Items.Count == 0)
            return "The invoice must have at least one item.";
        if (model.Items.Any(x => string.IsNullOrWhiteSpace(x.Description)))
            return "Item description is required.";
        if (model.Items.Any(x => x.Quantity <= 0))
            return "Item quantity must be greater than zero.";
        if (model.Items.Any(x => x.UnitPrice < 0))
            return "Item unit price cannot be negative.";
        if (model.Subtotal <= 0)
            return "Subtotal must be greater than zero.";
        if (model.Tax < 0)
            return "Tax cannot be negative.";
        if (string.IsNullOrWhiteSpace(model.Currency))
            return "Currency is required.";
        return null;
    }

    public async Task<byte[]> ExportPdfAsync(int id, CancellationToken cancellationToken = default)
    {
        var model = await GetReportModelAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        return await pdfExportService.GeneratePdfAsync(model, cancellationToken);
    }

    public async Task<string?> SavePdfToDiskAsync(int id, CancellationToken cancellationToken = default)
    {
        var model = await GetReportModelAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        var company = model.Company;
        var invoice = model.Invoice;
        var folderName = $"{invoice.IssueYear}-{invoice.IssueMonth:D2}";
        var fileName = $"{folderName}-{ExtractSequence(invoice.InvoiceNumber):D3}.pdf";
        var outputPath = Path.Combine(company.OutputBasePath, folderName, "PDF", fileName);

        await pdfExportService.SavePdfAsync(model, outputPath, cancellationToken);

        invoice.PdfPath = outputPath;
        await db.SaveChangesAsync(cancellationToken);
        return outputPath;
    }

    private static int ExtractSequence(string invoiceNumber)
    {
        var parts = invoiceNumber.Split('-');
        return parts.Length >= 4 && int.TryParse(parts[^1], out var seq) ? seq : 0;
    }
}
