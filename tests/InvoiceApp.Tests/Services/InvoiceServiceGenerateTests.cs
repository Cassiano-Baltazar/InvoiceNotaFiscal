using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Tests.Services;

public class InvoiceServiceGenerateTests : ServiceTestBase
{
    private static GenerateInvoicesRequest Request(int year = 2026, int month = 10, bool exportPdf = false) =>
        new() { IssueYear = year, IssueMonth = month, ExportPdf = exportPdf };

    [Fact]
    public async Task Creates_one_invoice_per_active_client_with_sequential_numbers()
    {
        Database.SeedCompany();
        Database.SeedClient("Alpha");
        Database.SeedClient("Inactive", isActive: false);
        Database.SeedClient("Beta");

        var result = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request());

        Assert.Equal(2, result.CreatedCount);
        Assert.Empty(result.Errors);
        Assert.Equal(["INV-2026-10-001", "INV-2026-10-002"], result.InvoiceNumbers);
        var invoices = await NewContext().Invoices.OrderBy(x => x.InvoiceNumber).ToListAsync();
        Assert.Equal(["Alpha", "Beta"], invoices.Select(x => x.ClientName));
    }

    [Fact]
    public async Task Copies_client_data_and_computes_dates_from_settings()
    {
        Database.SeedCompany(c => { c.IssueDay = 5; c.PaymentTermsDays = 10; });
        var client = Database.SeedClient("Alpha", amount: 4000m, currency: "GBP");

        await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request(2026, 10));

        var invoice = await NewContext().Invoices.SingleAsync();
        Assert.Equal(client.Id, invoice.ClientId);
        Assert.Equal("Alpha", invoice.ClientName);
        Assert.Equal("1 Main St", invoice.ClientAddressLine1);
        Assert.Equal("Montreal", invoice.ClientCity);
        Assert.Equal("Canada", invoice.ClientCountry);
        Assert.Equal("GBP", invoice.Currency);
        Assert.Equal("GBP label", invoice.CurrencyLabel);
        Assert.Equal(4000m, invoice.Subtotal);
        Assert.Equal(0m, invoice.Tax);
        Assert.Equal(4000m, invoice.Total);
        Assert.Equal("https://wise.com/pay/test", invoice.WisePaymentLink);
        Assert.Equal(new DateOnly(2026, 10, 5), invoice.InvoiceDate);
        Assert.Equal(new DateOnly(2026, 10, 15), invoice.DueDate);
        Assert.Equal(2026, invoice.ServiceYear);
        Assert.Equal(9, invoice.ServiceMonth);
        Assert.Equal("Software development services — September 2026", invoice.ServiceDescription);
        var item = Assert.Single(await NewContext().InvoiceItems.Where(x => x.InvoiceId == invoice.Id).ToListAsync());
        Assert.Equal(1, item.Position);
        Assert.Equal(invoice.ServiceDescription, item.Description);
        Assert.Equal(1m, item.Quantity);
        Assert.Equal("month", item.Unit);
        Assert.Equal(4000m, item.UnitPrice);
        Assert.Equal(4000m, item.Amount);
        Assert.Equal(InvoiceStatus.Generated, invoice.Status);
    }

    [Fact]
    public async Task January_issue_bills_december_of_previous_year()
    {
        Database.SeedCompany();
        Database.SeedClient();

        await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request(2027, 1));

        var invoice = await NewContext().Invoices.SingleAsync();
        Assert.Equal(2026, invoice.ServiceYear);
        Assert.Equal(12, invoice.ServiceMonth);
        Assert.Equal("INV-2027-01-001", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task Issue_day_is_limited_to_last_day_of_month()
    {
        Database.SeedCompany(c => c.IssueDay = 31);
        Database.SeedClient();

        await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request(2026, 2));

        var invoice = await NewContext().Invoices.SingleAsync();
        Assert.Equal(new DateOnly(2026, 2, 28), invoice.InvoiceDate);
    }

    [Fact]
    public async Task Skips_clients_that_already_have_an_invoice_in_the_month()
    {
        Database.SeedCompany();
        var alpha = Database.SeedClient("Alpha");
        Database.SeedClient("Beta");
        Database.SeedInvoice(alpha.Id);

        var result = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request());

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(["INV-2026-10-002"], result.InvoiceNumbers);
        Assert.Contains(result.Errors, e => e.Contains("already exists for Alpha"));
    }

    [Fact]
    public async Task Returns_error_when_there_are_no_active_clients()
    {
        Database.SeedCompany();
        Database.SeedClient(isActive: false);

        var result = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request());

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(["No active clients found."], result.Errors);
    }

    [Fact]
    public async Task Exports_pdf_to_month_folder_when_requested()
    {
        var company = Database.SeedCompany();
        Database.SeedClient();

        await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request(exportPdf: true));

        var expected = Path.Combine(company.OutputBasePath, "2026-10", "PDF", "2026-10-001.pdf");
        Assert.Equal([expected], Pdf.SavedPaths);
        Assert.True(File.Exists(expected));
        Assert.Equal(expected, (await NewContext().Invoices.SingleAsync()).PdfPath);
    }

    [Fact]
    public async Task Does_not_export_pdf_when_not_requested()
    {
        Database.SeedCompany();
        Database.SeedClient();

        await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request(exportPdf: false));

        Assert.Empty(Pdf.SavedPaths);
        Assert.Null((await NewContext().Invoices.SingleAsync()).PdfPath);
    }

    [Fact]
    public async Task Keeps_invoice_and_reports_error_when_pdf_export_fails()
    {
        Database.SeedCompany();
        Database.SeedClient();
        Pdf.ShouldFail = true;

        var result = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(Request(exportPdf: true));

        Assert.Equal(1, result.CreatedCount);
        Assert.Contains(result.Errors, e => e.StartsWith("PDF export failed for INV-2026-10-001"));
        Assert.Equal(1, await NewContext().Invoices.CountAsync());
    }
}
