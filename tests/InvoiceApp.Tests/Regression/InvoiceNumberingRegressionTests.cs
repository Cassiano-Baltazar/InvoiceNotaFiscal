using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Tests.Regression;

/// <summary>
/// Regression: the next invoice number was computed from the COUNT of invoices in the month.
/// After deleting an invoice the count went down, the generator produced a number that already
/// existed and saving failed on the unique index of InvoiceNumber.
/// Fix: the sequence is now the highest existing sequence in the month + 1.
/// </summary>
public class InvoiceNumberingRegressionTests : ServiceTestBase
{
    private static GenerateInvoicesRequest October() =>
        new() { IssueYear = 2026, IssueMonth = 10, ExportPdf = false };

    [Fact]
    public async Task Generating_after_deleting_the_first_invoice_does_not_duplicate_numbers()
    {
        Database.SeedCompany();
        var alpha = Database.SeedClient("Alpha");
        var beta = Database.SeedClient("Beta");
        Database.SeedClient("Gamma");
        var first = Database.SeedInvoice(alpha.Id, number: "INV-2026-10-001");
        Database.SeedInvoice(beta.Id, number: "INV-2026-10-002");
        await CreateInvoiceService().DeleteAsync(first.Id);

        var result = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(October());

        Assert.Empty(result.Errors.Where(e => !e.Contains("already exists")));
        Assert.Equal(["INV-2026-10-003", "INV-2026-10-004"], result.InvoiceNumbers);
    }

    [Fact]
    public async Task Same_flow_through_the_app_generate_delete_add_client_generate_again()
    {
        Database.SeedCompany();
        Database.SeedClient("Alpha");
        Database.SeedClient("Beta");

        var firstRun = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(October());
        Assert.Equal(["INV-2026-10-001", "INV-2026-10-002"], firstRun.InvoiceNumbers);

        int firstId;
        using (var db = Database.CreateContext())
            firstId = db.Invoices.Single(x => x.InvoiceNumber == "INV-2026-10-001").Id;
        Assert.True((await CreateInvoiceService().DeleteAsync(firstId)).Success);
        Database.SeedClient("Gamma");

        var secondRun = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(October());

        Assert.Equal(["INV-2026-10-003", "INV-2026-10-004"], secondRun.InvoiceNumbers);
        using var check = Database.CreateContext();
        var numbers = await check.Invoices.Select(x => x.InvoiceNumber).ToListAsync();
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Gaps_in_the_sequence_continue_after_the_highest_number()
    {
        Database.SeedCompany();
        var alpha = Database.SeedClient("Alpha");
        Database.SeedClient("Beta");
        Database.SeedInvoice(alpha.Id, number: "INV-2026-10-007");

        var result = await CreateInvoiceService().GenerateMonthlyInvoicesAsync(October());

        Assert.Equal(["INV-2026-10-008"], result.InvoiceNumbers);
    }
}
