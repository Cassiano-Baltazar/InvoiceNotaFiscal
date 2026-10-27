using Bunit;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Components.Pages.Invoices;
using InvoiceApp.Web.Data.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Index = InvoiceApp.Web.Components.Pages.Invoices.Index;

namespace InvoiceApp.Tests.Components;

public class InvoicePagesTests : ComponentTestBase
{
    private readonly Client alpha;
    private readonly Client beta;

    public InvoicePagesTests()
    {
        Database.SeedCompany();
        alpha = Database.SeedClient("Alpha");
        beta = Database.SeedClient("Beta");
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    // ---------- List ----------

    [Fact]
    public void List_shows_invoices_with_status_and_actions()
    {
        Database.SeedInvoice(alpha.Id, InvoiceStatus.Sent);
        Database.SeedInvoice(beta.Id, InvoiceStatus.Paid);

        var cut = Render<Index>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Contains("Sent", cut.Find("tbody tr:nth-child(1) .status-badge").TextContent);
        Assert.Equal(3, cut.FindAll("tbody tr:nth-child(1) [data-action]").Count);
        Assert.Empty(cut.FindAll("tbody tr:nth-child(2) [data-action]"));
    }

    [Fact]
    public void List_action_reloads_rows_and_shows_message()
    {
        Database.SeedInvoice(alpha.Id);
        var cut = Render<Index>();

        cut.Find("[data-action=mark-sent]").Click();

        cut.WaitForAssertion(() => Assert.Contains("marked as sent", cut.Find(".message").TextContent));
        Assert.Contains("Sent", cut.Find(".status-badge").TextContent);
    }

    [Fact]
    public void List_delete_removes_row()
    {
        ConfirmDialogsReturn(true);
        Database.SeedInvoice(alpha.Id);
        var cut = Render<Index>();

        cut.Find("[data-action=delete]").Click();

        cut.WaitForAssertion(() => Assert.Contains("No invoices found.", cut.Markup));
    }

    [Fact]
    public void List_filters_by_status_and_clear_resets()
    {
        Database.SeedInvoice(alpha.Id, InvoiceStatus.Sent);
        Database.SeedInvoice(beta.Id, InvoiceStatus.Cancelled);
        var cut = Render<Index>();

        cut.FindAll("select")[1].Change(nameof(InvoiceStatus.Cancelled));
        cut.FindAll("button").First(b => b.TextContent == "Filter").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Contains("Beta", cut.Find("tbody tr").TextContent);

        cut.FindAll("button").First(b => b.TextContent == "Clear").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
    }

    [Fact]
    public void List_status_filter_uses_friendly_labels()
    {
        var cut = Render<Index>();

        var options = cut.FindAll("select")[1].QuerySelectorAll("option").Select(o => o.TextContent);
        Assert.Equal(["All", "Not Sent", "Sent", "Paid", "Cancelled"], options);
    }

    // ---------- Detail ----------

    [Fact]
    public void Detail_shows_invoice_and_actions()
    {
        var invoice = Database.SeedInvoice(alpha.Id);

        var cut = Render<Detail>(p => p.Add(x => x.Id, invoice.Id));

        cut.WaitForAssertion(() => Assert.Contains(invoice.InvoiceNumber, cut.Find("h1").TextContent));
        Assert.Contains("Not Sent", cut.Find(".status-badge").TextContent);
        Assert.Equal(5, cut.FindAll("[data-action]").Count);
    }

    [Fact]
    public void Detail_paid_invoice_has_no_actions()
    {
        var invoice = Database.SeedInvoice(alpha.Id, InvoiceStatus.Paid);

        var cut = Render<Detail>(p => p.Add(x => x.Id, invoice.Id));

        Assert.Empty(cut.FindAll("[data-action]"));
    }

    [Fact]
    public void Detail_action_refreshes_status()
    {
        var invoice = Database.SeedInvoice(alpha.Id);
        var cut = Render<Detail>(p => p.Add(x => x.Id, invoice.Id));

        cut.Find("[data-action=mark-paid]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Paid", cut.Find(".status-badge").TextContent));
        Assert.Empty(cut.FindAll("[data-action]"));
    }

    [Fact]
    public void Detail_delete_navigates_to_list()
    {
        ConfirmDialogsReturn(true);
        var invoice = Database.SeedInvoice(alpha.Id);
        var cut = Render<Detail>(p => p.Add(x => x.Id, invoice.Id));

        cut.Find("[data-action=delete]").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/invoices", Navigation.Uri));
    }

    [Fact]
    public void Detail_save_pdf_shows_path()
    {
        var invoice = Database.SeedInvoice(alpha.Id);
        var cut = Render<Detail>(p => p.Add(x => x.Id, invoice.Id));

        cut.FindAll("button").First(b => b.TextContent == "Save PDF to Folder").Click();

        cut.WaitForAssertion(() => Assert.Contains("PDF saved to", cut.Find(".message").TextContent));
        Assert.Contains("Saved at:", cut.Markup);
    }

    [Fact]
    public void Detail_save_pdf_shows_error_when_export_fails()
    {
        Pdf.ShouldFail = true;
        var invoice = Database.SeedInvoice(alpha.Id);
        var cut = Render<Detail>(p => p.Add(x => x.Id, invoice.Id));

        cut.FindAll("button").First(b => b.TextContent == "Save PDF to Folder").Click();

        cut.WaitForAssertion(() => Assert.Contains("Failed to save PDF", cut.Find(".message-error").TextContent));
    }

    [Fact]
    public void Detail_unknown_invoice_shows_not_found()
    {
        var cut = Render<Detail>(p => p.Add(x => x.Id, 999));

        Assert.Contains("Invoice not found.", cut.Markup);
    }

    // ---------- Edit ----------

    [Fact]
    public void Edit_sent_invoice_warns_saves_and_sets_not_sent()
    {
        var invoice = Database.SeedInvoice(alpha.Id, InvoiceStatus.Sent);
        var cut = Render<Edit>(p => p.Add(x => x.Id, invoice.Id));

        Assert.Contains("set it back to Not Sent", cut.Find(".message-warning").TextContent);
        cut.Find(".item-price").Change("2500");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains($"/invoices/{invoice.Id}?saved=", Navigation.Uri));
        var saved = Database.GetInvoice(invoice.Id);
        Assert.Equal(2500m, saved.Total);
        Assert.Equal(InvoiceStatus.Generated, saved.Status);
    }

    [Fact]
    public void Edit_adds_and_removes_items_and_updates_totals()
    {
        var invoice = Database.SeedInvoice(alpha.Id);
        var cut = Render<Edit>(p => p.Add(x => x.Id, invoice.Id));
        Assert.Single(cut.FindAll(".item-row"));
        Assert.True(cut.Find("[data-action=remove-item]").HasAttribute("disabled"));

        cut.Find("[data-action=add-item]").Click();
        var rows = cut.FindAll(".item-row");
        Assert.Equal(2, rows.Count);
        Assert.Equal("month", cut.FindAll(".item-unit")[1].GetAttribute("value"));

        cut.FindAll(".item-description")[1].Change("Extra hours");
        cut.FindAll(".item-quantity")[1].Change("4");
        cut.FindAll(".item-unit")[1].Change("hour");
        cut.FindAll(".item-price")[1].Change("50");
        cut.Find("[data-field=tax]").Change("10");

        Assert.Equal("200.00", cut.FindAll(".item-amount")[1].TextContent);
        Assert.Equal("1,200.00", cut.Find("[data-field=subtotal]").GetAttribute("value"));
        Assert.Equal("1,210.00", cut.Find("[data-field=total]").GetAttribute("value"));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("saved=", Navigation.Uri));
        var saved = Database.GetInvoice(invoice.Id);
        Assert.Equal(2, saved.Items.Count);
        Assert.Equal(1210m, saved.Total);
    }

    [Fact]
    public void Edit_remove_item_drops_the_row()
    {
        var invoice = Database.SeedInvoice(alpha.Id);
        var cut = Render<Edit>(p => p.Add(x => x.Id, invoice.Id));
        cut.Find("[data-action=add-item]").Click();
        cut.FindAll(".item-description")[1].Change("Second");

        cut.FindAll("[data-action=remove-item]")[0].Click();

        Assert.Single(cut.FindAll(".item-row"));
        Assert.Equal("Second", cut.Find(".item-description").GetAttribute("value"));
    }

    [Fact]
    public void Edit_shows_item_validation_error_from_service()
    {
        var invoice = Database.SeedInvoice(alpha.Id);
        var cut = Render<Edit>(p => p.Add(x => x.Id, invoice.Id));
        cut.Find("[data-action=add-item]").Click();
        cut.FindAll(".item-price")[1].Change("10");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Item description is required.", cut.Find(".message-error").TextContent));
        Assert.Single(Database.GetInvoice(invoice.Id).Items);
    }

    [Fact]
    public void Detail_shows_every_item()
    {
        var invoice = Database.SeedInvoice(alpha.Id);
        using (var db = Database.CreateContext())
        {
            db.InvoiceItems.Add(new InvoiceItem { InvoiceId = invoice.Id, Position = 2, Description = "Extra hours", Quantity = 3, Unit = "hour", UnitPrice = 20, Amount = 60 });
            db.SaveChanges();
        }

        var cut = Render<Detail>(p => p.Add(x => x.Id, invoice.Id));

        Assert.Contains("Services", cut.Markup);
        Assert.Contains("Extra hours", cut.Markup);
        Assert.Contains("3 hour", cut.Markup);
        Assert.Contains("1 month", cut.Markup);
    }

    [Fact]
    public void Edit_shows_service_error_and_does_not_navigate()
    {
        var invoice = Database.SeedInvoice(alpha.Id);
        var cut = Render<Edit>(p => p.Add(x => x.Id, invoice.Id));

        cut.FindAll("input")[1].Change("2026-09-01");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Due date cannot be before", cut.Find(".message-error").TextContent));
        Assert.DoesNotContain("saved=", Navigation.Uri);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void Edit_is_blocked_for_paid_and_cancelled(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(alpha.Id, status);

        var cut = Render<Edit>(p => p.Add(x => x.Id, invoice.Id));

        Assert.Contains("cannot be edited", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void Edit_unknown_invoice_shows_not_found()
    {
        var cut = Render<Edit>(p => p.Add(x => x.Id, 999));

        Assert.Contains("Invoice not found.", cut.Markup);
    }

    // ---------- Generate ----------

    [Fact]
    public void Generate_creates_invoices_and_lists_numbers()
    {
        var cut = Render<Generate>();
        Assert.Contains("Active clients:</strong> 2", cut.Markup);

        cut.FindAll("input")[0].Change("2026");
        cut.FindAll("input")[1].Change("10");
        cut.Find("input[type=checkbox]").Change(false);
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate Invoices")).Click();

        cut.WaitForAssertion(() => Assert.Contains("2 invoice(s) generated successfully.", cut.Markup));
        Assert.Equal(["INV-2026-10-001", "INV-2026-10-002"], cut.FindAll(".success-list li").Select(x => x.TextContent));
        Assert.Contains("September 2026", cut.Markup);
    }
}
