using Bunit;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Components.Shared;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;

namespace InvoiceApp.Tests.Components;

public class InvoiceActionsTests : ComponentTestBase
{
    private readonly Client client;

    public InvoiceActionsTests()
    {
        Database.SeedCompany();
        client = Database.SeedClient();
    }

    private static string[] VisibleActions(IRenderedComponent<InvoiceActions> cut) =>
        cut.FindAll("[data-action]").Select(e => e.GetAttribute("data-action")!).ToArray();

    [Theory]
    [InlineData(InvoiceStatus.Generated, new[] { "edit", "mark-sent", "mark-paid", "cancel", "delete" })]
    [InlineData(InvoiceStatus.Sent, new[] { "edit", "mark-paid", "cancel" })]
    [InlineData(InvoiceStatus.Paid, new string[0])]
    [InlineData(InvoiceStatus.Cancelled, new[] { "reactivate" })]
    public void Shows_only_allowed_actions(InvoiceStatus status, string[] expected)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var cut = Render<InvoiceActions>(p => p.Add(x => x.Invoice, invoice));

        Assert.Equal(expected, VisibleActions(cut));
    }

    [Fact]
    public void Edit_links_to_edit_page()
    {
        var invoice = Database.SeedInvoice(client.Id);

        var cut = Render<InvoiceActions>(p => p.Add(x => x.Invoice, invoice));

        Assert.Equal($"/invoices/{invoice.Id}/edit", cut.Find("[data-action=edit]").GetAttribute("href"));
    }

    [Theory]
    [InlineData("mark-sent", InvoiceStatus.Sent)]
    [InlineData("mark-paid", InvoiceStatus.Paid)]
    public void Status_buttons_update_invoice_and_notify(string action, InvoiceStatus expected)
    {
        var invoice = Database.SeedInvoice(client.Id);
        InvoiceActionResult? received = null;
        var cut = Render<InvoiceActions>(p => p
            .Add(x => x.Invoice, invoice)
            .Add(x => x.OnChanged, r => received = r));

        cut.Find($"[data-action={action}]").Click();

        Assert.True(received?.Success);
        Assert.Equal(expected, Database.GetInvoice(invoice.Id).Status);
    }

    [Fact]
    public void Reactivate_button_restores_cancelled_invoice()
    {
        var invoice = Database.SeedInvoice(client.Id, InvoiceStatus.Cancelled);
        var cut = Render<InvoiceActions>(p => p.Add(x => x.Invoice, invoice));

        cut.Find("[data-action=reactivate]").Click();

        Assert.Equal(InvoiceStatus.Generated, Database.GetInvoice(invoice.Id).Status);
    }

    [Theory]
    [InlineData(true, InvoiceStatus.Cancelled)]
    [InlineData(false, InvoiceStatus.Sent)]
    public void Cancel_asks_for_confirmation(bool confirmed, InvoiceStatus expected)
    {
        ConfirmDialogsReturn(confirmed);
        var invoice = Database.SeedInvoice(client.Id, InvoiceStatus.Sent);
        var cut = Render<InvoiceActions>(p => p.Add(x => x.Invoice, invoice));

        cut.Find("[data-action=cancel]").Click();

        Assert.Equal(expected, Database.GetInvoice(invoice.Id).Status);
    }

    [Fact]
    public void Delete_confirmed_removes_invoice_and_raises_on_deleted()
    {
        ConfirmDialogsReturn(true);
        var invoice = Database.SeedInvoice(client.Id);
        var deleted = false;
        var cut = Render<InvoiceActions>(p => p
            .Add(x => x.Invoice, invoice)
            .Add(x => x.OnDeleted, _ => deleted = true));

        cut.Find("[data-action=delete]").Click();

        Assert.True(deleted);
        using var db = Database.CreateContext();
        Assert.Empty(db.Invoices);
    }

    [Fact]
    public void Delete_not_confirmed_keeps_invoice()
    {
        ConfirmDialogsReturn(false);
        var invoice = Database.SeedInvoice(client.Id);
        var cut = Render<InvoiceActions>(p => p.Add(x => x.Invoice, invoice));

        cut.Find("[data-action=delete]").Click();

        using var db = Database.CreateContext();
        Assert.Single(db.Invoices);
    }

    [Fact]
    public void Failed_action_is_reported_through_on_changed()
    {
        // The screen is stale: the invoice was paid in the meantime.
        var invoice = Database.SeedInvoice(client.Id);
        InvoiceActionResult? received = null;
        var cut = Render<InvoiceActions>(p => p
            .Add(x => x.Invoice, invoice)
            .Add(x => x.OnChanged, r => received = r));
        using (var db = Database.CreateContext())
        {
            db.Invoices.Single().Status = InvoiceStatus.Paid;
            db.SaveChanges();
        }

        cut.Find("[data-action=mark-sent]").Click();

        Assert.False(received?.Success);
        Assert.Equal(InvoiceStatus.Paid, Database.GetInvoice(invoice.Id).Status);
    }
}
