using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Tests.Services;

/// <summary>Invoices created by hand, e.g. one job of a freelance client.</summary>
public class InvoiceServiceCreateTests : ServiceTestBase
{
    private static InvoiceCreateModel Model(int? clientId, decimal price = 440m, bool exportPdf = false) => new()
    {
        ClientId = clientId,
        InvoiceDate = new DateOnly(2026, 11, 16),
        DueDate = new DateOnly(2026, 12, 1),
        ServiceYear = 2026,
        ServiceMonth = 11,
        Currency = "AUD",
        CurrencyLabel = "Australian Dollars",
        WisePaymentLink = " https://wise.com/pay/jemb ",
        Tax = 0,
        ExportPdf = exportPdf,
        Items = [new InvoiceItemEditModel { Description = " Security review ", Quantity = 1, Unit = "job", UnitPrice = price }]
    };

    [Fact]
    public async Task Creates_invoice_for_freelance_client_with_the_job_amount()
    {
        Database.SeedCompany();
        var client = Database.SeedClient("Jemb", amount: 0m, currency: "AUD", billingType: ClientBillingType.Freelance);

        var result = await CreateInvoiceService().CreateAsync(Model(client.Id, 440m));

        Assert.True(result.Success, result.Message);
        Assert.Equal("Invoice INV-2026-11-001 created.", result.Message);
        var invoice = await NewContext().Invoices.Include(x => x.Items).SingleAsync();
        Assert.Equal(invoice.Id, result.InvoiceId);
        Assert.Equal("INV-2026-11-001", invoice.InvoiceNumber);
        Assert.Equal("Jemb", invoice.ClientName);
        Assert.Equal("1 Main St", invoice.ClientAddressLine1);
        Assert.Equal((2026, 11), (invoice.IssueYear, invoice.IssueMonth));
        Assert.Equal((2026, 11), (invoice.ServiceYear, invoice.ServiceMonth));
        Assert.Equal("AUD", invoice.Currency);
        Assert.Equal("https://wise.com/pay/jemb", invoice.WisePaymentLink);
        Assert.Equal(440m, invoice.Subtotal);
        Assert.Equal(440m, invoice.Total);
        Assert.Equal("Security review", invoice.ServiceDescription);
        Assert.Equal("job", Assert.Single(invoice.Items).Unit);
        Assert.Equal(InvoiceStatus.Generated, invoice.Status);
    }

    [Fact]
    public async Task Continues_the_numbering_of_the_issue_month_and_does_not_block_generate_month()
    {
        Database.SeedCompany();
        var monthly = Database.SeedClient("Monthly");
        var freelance = Database.SeedClient("Jemb", billingType: ClientBillingType.Freelance);

        var first = await CreateInvoiceService().CreateAsync(Model(freelance.Id));
        var generated = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(
            new GenerateInvoicesRequest { IssueYear = 2026, IssueMonth = 11, ExportPdf = false });
        var second = await CreateInvoiceService().CreateAsync(Model(freelance.Id, 100m));

        Assert.Equal("Invoice INV-2026-11-001 created.", first.Message);
        Assert.Equal(["INV-2026-11-002"], generated.InvoiceNumbers);
        Assert.Empty(generated.Errors);
        Assert.Equal("Invoice INV-2026-11-003 created.", second.Message);
        Assert.Equal(monthly.Id, (await NewContext().Invoices.SingleAsync(x => x.InvoiceNumber == "INV-2026-11-002")).ClientId);
    }

    [Fact]
    public async Task Exports_pdf_when_requested()
    {
        Database.SeedCompany();
        var client = Database.SeedClient("Jemb", billingType: ClientBillingType.Freelance);

        var result = await CreateInvoiceService().CreateAsync(Model(client.Id, exportPdf: true));

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        Assert.NotNull((await NewContext().Invoices.SingleAsync()).PdfPath);
    }

    [Fact]
    public async Task Requires_a_client()
    {
        Database.SeedCompany();

        var result = await CreateInvoiceService().CreateAsync(Model(null));

        Assert.False(result.Success);
        Assert.Equal("Select a client.", result.Message);
        Assert.Empty(await NewContext().Invoices.ToListAsync());
    }

    [Fact]
    public async Task Rejects_an_invoice_with_no_amount()
    {
        Database.SeedCompany();
        var client = Database.SeedClient("Jemb", billingType: ClientBillingType.Freelance);

        var result = await CreateInvoiceService().CreateAsync(Model(client.Id, 0m));

        Assert.False(result.Success);
        Assert.Equal("Subtotal must be greater than zero.", result.Message);
    }

    [Fact]
    public async Task Rejects_an_invalid_service_month()
    {
        Database.SeedCompany();
        var client = Database.SeedClient("Jemb", billingType: ClientBillingType.Freelance);
        var model = Model(client.Id);
        model.ServiceMonth = 13;

        var result = await CreateInvoiceService().CreateAsync(model);

        Assert.False(result.Success);
        Assert.Equal("Service month is not valid.", result.Message);
    }
}
