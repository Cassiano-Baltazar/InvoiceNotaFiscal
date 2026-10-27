using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;

namespace InvoiceApp.Tests.Services;

public class InvoiceServiceActionTests : ServiceTestBase
{
    private readonly Client client;

    public InvoiceServiceActionTests()
    {
        Database.SeedCompany();
        client = Database.SeedClient();
    }

    private static InvoiceEditModel ValidEdit() => new()
    {
        InvoiceDate = new DateOnly(2026, 10, 5),
        DueDate = new DateOnly(2026, 10, 20),
        Currency = " USD ",
        CurrencyLabel = "US Dollars",
        Tax = 100m,
        WisePaymentLink = "https://wise.com/pay/new",
        Items =
        [
            new() { Description = " New description ", Quantity = 1, Unit = "month", UnitPrice = 1000m },
            new() { Description = "Extra hours", Quantity = 10, Unit = " hour ", UnitPrice = 50m }
        ]
    };

    // ---------- Edit ----------

    [Fact]
    public async Task Edit_not_sent_invoice_updates_fields_and_total()
    {
        var invoice = Database.SeedInvoice(client.Id);

        var result = await CreateInvoiceService().UpdateAsync(invoice.Id, ValidEdit());

        Assert.True(result.Success);
        Assert.Equal("Invoice updated.", result.Message);
        var saved = Database.GetInvoice(invoice.Id);
        Assert.Equal(new DateOnly(2026, 10, 5), saved.InvoiceDate);
        Assert.Equal(new DateOnly(2026, 10, 20), saved.DueDate);
        Assert.Equal("USD", saved.Currency);
        Assert.Equal("US Dollars", saved.CurrencyLabel);
        Assert.Equal(1500m, saved.Subtotal);
        Assert.Equal(100m, saved.Tax);
        Assert.Equal(1600m, saved.Total);
        Assert.Equal("New description", saved.ServiceDescription);
        Assert.Collection(saved.Items.OrderBy(x => x.Position),
            first =>
            {
                Assert.Equal(1, first.Position);
                Assert.Equal("New description", first.Description);
                Assert.Equal(1m, first.Quantity);
                Assert.Equal("month", first.Unit);
                Assert.Equal(1000m, first.UnitPrice);
                Assert.Equal(1000m, first.Amount);
            },
            second =>
            {
                Assert.Equal(2, second.Position);
                Assert.Equal("Extra hours", second.Description);
                Assert.Equal(10m, second.Quantity);
                Assert.Equal("hour", second.Unit);
                Assert.Equal(500m, second.Amount);
            });
        Assert.Equal("https://wise.com/pay/new", saved.WisePaymentLink);
        Assert.Equal(InvoiceStatus.Generated, saved.Status);
    }

    [Fact]
    public async Task Edit_sent_invoice_changes_status_back_to_not_sent()
    {
        var invoice = Database.SeedInvoice(client.Id, InvoiceStatus.Sent);

        var result = await CreateInvoiceService().UpdateAsync(invoice.Id, ValidEdit());

        Assert.True(result.Success);
        Assert.Contains("Not Sent", result.Message);
        Assert.Equal(InvoiceStatus.Generated, Database.GetInvoice(invoice.Id).Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task Edit_is_blocked_for_paid_and_cancelled(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().UpdateAsync(invoice.Id, ValidEdit());

        Assert.False(result.Success);
        Assert.Contains("cannot be edited", result.Message);
        var saved = Database.GetInvoice(invoice.Id);
        Assert.Equal(status, saved.Status);
        Assert.Equal(1000m, saved.Total);
    }

    public static TheoryData<string, Action<InvoiceEditModel>> InvalidEdits => new()
    {
        { "Due date", m => m.DueDate = m.InvoiceDate.AddDays(-1) },
        { "Subtotal", m => m.Items.ForEach(i => i.UnitPrice = 0) },
        { "The invoice must have at least one item", m => m.Items.Clear() },
        { "Item description", m => m.Items[1].Description = " " },
        { "Item quantity", m => m.Items[1].Quantity = 0 },
        { "Item unit price", m => m.Items[1].UnitPrice = -1 },
        { "Tax", m => m.Tax = -1 },
        { "Currency", m => m.Currency = " " },
    };

    [Theory]
    [MemberData(nameof(InvalidEdits))]
    public async Task Edit_rejects_invalid_data(string expectedMessageStart, Action<InvoiceEditModel> breakModel)
    {
        var invoice = Database.SeedInvoice(client.Id, InvoiceStatus.Sent);
        var model = ValidEdit();
        breakModel(model);

        var result = await CreateInvoiceService().UpdateAsync(invoice.Id, model);

        Assert.False(result.Success);
        Assert.StartsWith(expectedMessageStart, result.Message);
        Assert.Equal(InvoiceStatus.Sent, Database.GetInvoice(invoice.Id).Status);
    }

    [Fact]
    public async Task Edit_regenerates_pdf_when_one_was_saved()
    {
        var invoice = Database.SeedInvoice(client.Id, pdfPath: "old.pdf");

        var result = await CreateInvoiceService().UpdateAsync(invoice.Id, ValidEdit());

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        var saved = Database.GetInvoice(invoice.Id);
        Assert.Single(Pdf.SavedPaths);
        Assert.Equal(Pdf.SavedPaths[0], saved.PdfPath);
        Assert.True(File.Exists(saved.PdfPath));
    }

    [Fact]
    public async Task Edit_does_not_create_pdf_when_none_was_saved()
    {
        var invoice = Database.SeedInvoice(client.Id);

        await CreateInvoiceService().UpdateAsync(invoice.Id, ValidEdit());

        Assert.Empty(Pdf.SavedPaths);
    }

    [Fact]
    public async Task Edit_returns_warning_when_pdf_regeneration_fails()
    {
        var invoice = Database.SeedInvoice(client.Id, pdfPath: "old.pdf");
        Pdf.ShouldFail = true;

        var result = await CreateInvoiceService().UpdateAsync(invoice.Id, ValidEdit());

        Assert.True(result.Success);
        Assert.Contains("could not be regenerated", result.Warning);
        Assert.Equal(1600m, Database.GetInvoice(invoice.Id).Total);
    }

    [Fact]
    public async Task Edit_can_remove_items_and_recalculates_totals()
    {
        var invoice = Database.SeedInvoice(client.Id);
        await CreateInvoiceService().UpdateAsync(invoice.Id, ValidEdit());

        var model = ValidEdit();
        model.Items.RemoveAt(0);
        model.Tax = 0;
        var result = await CreateInvoiceService().UpdateAsync(invoice.Id, model);

        Assert.True(result.Success);
        var saved = Database.GetInvoice(invoice.Id);
        var item = Assert.Single(saved.Items);
        Assert.Equal(1, item.Position);
        Assert.Equal("Extra hours", item.Description);
        Assert.Equal(500m, saved.Subtotal);
        Assert.Equal(500m, saved.Total);
        Assert.Equal("Extra hours", saved.ServiceDescription);
        using var db = Database.CreateContext();
        Assert.Single(db.InvoiceItems);
    }

    [Fact]
    public void Item_amount_is_rounded_to_cents()
    {
        var item = new InvoiceItemEditModel { Quantity = 1.5m, UnitPrice = 33.333m };

        Assert.Equal(50.00m, item.Amount);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_not_sent_invoice_removes_it_and_its_pdf()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"invoice-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(pdfPath, "pdf");
        var invoice = Database.SeedInvoice(client.Id, pdfPath: pdfPath);

        var result = await CreateInvoiceService().DeleteAsync(invoice.Id);

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        Assert.Null(await NewContext().Invoices.FindAsync(invoice.Id));
        Assert.Empty(NewContext().InvoiceItems);
        Assert.False(File.Exists(pdfPath));
    }

    [Fact]
    public async Task Delete_works_when_pdf_file_is_missing()
    {
        var invoice = Database.SeedInvoice(client.Id, pdfPath: Path.Combine(Path.GetTempPath(), "does-not-exist.pdf"));

        var result = await CreateInvoiceService().DeleteAsync(invoice.Id);

        Assert.True(result.Success);
        Assert.Null(await NewContext().Invoices.FindAsync(invoice.Id));
    }

    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task Delete_is_blocked_unless_not_sent(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().DeleteAsync(invoice.Id);

        Assert.False(result.Success);
        Assert.Contains("cannot be deleted", result.Message);
        Assert.NotNull(await NewContext().Invoices.FindAsync(invoice.Id));
    }

    // ---------- Status changes ----------

    [Theory]
    [InlineData(InvoiceStatus.Generated)]
    [InlineData(InvoiceStatus.Sent)]
    public async Task Cancel_works_for_not_sent_and_sent(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().CancelAsync(invoice.Id);

        Assert.True(result.Success);
        Assert.Equal(InvoiceStatus.Cancelled, Database.GetInvoice(invoice.Id).Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task Cancel_is_blocked_for_paid_and_cancelled(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().CancelAsync(invoice.Id);

        Assert.False(result.Success);
        Assert.Equal(status, Database.GetInvoice(invoice.Id).Status);
    }

    [Fact]
    public async Task Reactivate_cancelled_invoice_goes_back_to_not_sent()
    {
        var invoice = Database.SeedInvoice(client.Id, InvoiceStatus.Cancelled);

        var result = await CreateInvoiceService().ReactivateAsync(invoice.Id);

        Assert.True(result.Success);
        Assert.Equal(InvoiceStatus.Generated, Database.GetInvoice(invoice.Id).Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Generated)]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Paid)]
    public async Task Reactivate_only_works_for_cancelled(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().ReactivateAsync(invoice.Id);

        Assert.False(result.Success);
        Assert.Equal(status, Database.GetInvoice(invoice.Id).Status);
    }

    [Fact]
    public async Task Mark_as_sent_works_for_not_sent()
    {
        var invoice = Database.SeedInvoice(client.Id);

        var result = await CreateInvoiceService().MarkAsSentAsync(invoice.Id);

        Assert.True(result.Success);
        Assert.Equal(InvoiceStatus.Sent, Database.GetInvoice(invoice.Id).Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task Mark_as_sent_is_blocked_for_other_statuses(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().MarkAsSentAsync(invoice.Id);

        Assert.False(result.Success);
        Assert.Equal(status, Database.GetInvoice(invoice.Id).Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Generated)]
    [InlineData(InvoiceStatus.Sent)]
    public async Task Mark_as_paid_works_for_not_sent_and_sent(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().MarkAsPaidAsync(invoice.Id);

        Assert.True(result.Success);
        Assert.Equal(InvoiceStatus.Paid, Database.GetInvoice(invoice.Id).Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task Mark_as_paid_is_blocked_for_paid_and_cancelled(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().MarkAsPaidAsync(invoice.Id);

        Assert.False(result.Success);
        Assert.Equal(status, Database.GetInvoice(invoice.Id).Status);
    }

    [Fact]
    public async Task Every_action_reports_missing_invoice()
    {
        var service = CreateInvoiceService();

        var results = new[]
        {
            await service.UpdateAsync(999, ValidEdit()),
            await service.DeleteAsync(999),
            await service.CancelAsync(999),
            await service.ReactivateAsync(999),
            await service.MarkAsSentAsync(999),
            await service.MarkAsPaidAsync(999)
        };

        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Equal("Invoice not found.", r.Message);
        });
    }

    [Fact]
    public async Task Full_lifecycle_sent_cancelled_reactivated_sent_paid()
    {
        var invoice = Database.SeedInvoice(client.Id);
        var service = CreateInvoiceService();

        Assert.True((await service.MarkAsSentAsync(invoice.Id)).Success);
        Assert.False((await service.DeleteAsync(invoice.Id)).Success);
        Assert.True((await service.CancelAsync(invoice.Id)).Success);
        Assert.True((await service.ReactivateAsync(invoice.Id)).Success);
        Assert.True((await service.MarkAsSentAsync(invoice.Id)).Success);
        Assert.True((await service.MarkAsPaidAsync(invoice.Id)).Success);

        Assert.False((await service.UpdateAsync(invoice.Id, ValidEdit())).Success);
        Assert.False((await service.CancelAsync(invoice.Id)).Success);
        Assert.False((await service.DeleteAsync(invoice.Id)).Success);
        Assert.Equal(InvoiceStatus.Paid, Database.GetInvoice(invoice.Id).Status);
    }
}
