using Bunit;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Components.Pages.NfsePages;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Index = InvoiceApp.Web.Components.Pages.NfsePages.Index;
using InvoiceDetail = InvoiceApp.Web.Components.Pages.Invoices.Detail;
using NfseDetail = InvoiceApp.Web.Components.Pages.NfsePages.Detail;

namespace InvoiceApp.Tests.Components;

public class NfsePagesTests : ComponentTestBase
{
    private readonly Client canada;
    private readonly Client uk;

    public NfsePagesTests()
    {
        Database.SeedCompany(c => c.Cnpj = NfseFixtures.CompanyCnpj);
        canada = Database.SeedClient("Sample Client Canada Inc.", amount: 4000m, currency: "CAD");
        uk = Database.SeedClient("Sample Client UK Ltd", amount: 1000m, currency: "GBP");
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    // Invoices issued in October bill September (the service month of the fixtures).
    private Invoice SeptemberInvoice(Client client, InvoiceStatus status = InvoiceStatus.Paid) =>
        Database.SeedInvoice(client.Id, status, issueYear: 2026, issueMonth: 10);

    private int CreateDraft(Invoice invoice)
    {
        using var db = Database.CreateContext();
        return new NfseService(db).CreateDraftForInvoiceAsync(invoice.Id).GetAwaiter().GetResult().NfseId!.Value;
    }

    private int Import(string fixture, Invoice invoice)
    {
        using var db = Database.CreateContext();
        var result = new NfseService(db).ImportAsync(fixture, NfseFixtures.Read(fixture), invoice.Id).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message);
        return result.NfseId!.Value;
    }

    private List<Nfse> AllNfses()
    {
        using var db = Database.CreateContext();
        return db.Nfses.AsNoTracking().OrderBy(x => x.Id).ToList();
    }

    // ---------- List ----------

    [Fact]
    public void List_shows_notes_and_total_of_issued_only()
    {
        Import(NfseFixtures.IssuedCanada, SeptemberInvoice(canada));
        CreateDraft(SeptemberInvoice(uk));

        var cut = Render<Index>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Contains("R$ 15.207,14", cut.Find("tfoot").TextContent);
        Assert.Contains("Draft", cut.Markup);
    }

    [Fact]
    public void List_filters_by_status()
    {
        Import(NfseFixtures.IssuedCanada, SeptemberInvoice(canada));
        CreateDraft(SeptemberInvoice(uk));
        var cut = Render<Index>();

        cut.Find("select").Change(nameof(NfseStatus.Draft));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Contains("SAMPLE CLIENT UK LTD", cut.Find("tbody tr").TextContent);
    }

    [Fact]
    public void List_is_empty_without_notes()
    {
        var cut = Render<Index>();

        cut.WaitForAssertion(() => Assert.Contains("No NFS-e yet", cut.Markup));
    }

    // ---------- New ----------

    [Fact]
    public void New_creates_draft_for_selected_invoice_and_opens_it()
    {
        var invoice = SeptemberInvoice(canada);
        var cut = Render<New>();

        cut.Find("select").Change(invoice.Id.ToString());
        cut.FindAll("button").First(b => b.TextContent.Contains("Create Draft")).Click();

        cut.WaitForAssertion(() => Assert.Single(AllNfses()));
        var draft = AllNfses().Single();
        Assert.Contains($"/nfse/{draft.Id}", Navigation.Uri);
    }

    // ---------- Draft detail ----------

    [Fact]
    public void Draft_exchange_rate_fills_the_brl_amount_and_saves()
    {
        var id = CreateDraft(SeptemberInvoice(canada));
        var cut = Render<NfseDetail>(p => p.Add(x => x.Id, id));

        cut.Find("[data-field=exchange-rate]").Change("3.8018");
        Assert.Equal("15207.20", cut.Find("[data-field=amount-brl]").GetAttribute("value"));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("saved", cut.Find(".message").TextContent));
        Assert.Equal(15207.20m, AllNfses().Single().AmountBrl);
    }

    [Fact]
    public void Draft_with_invalid_data_shows_the_error()
    {
        var id = CreateDraft(SeptemberInvoice(canada));
        var cut = Render<NfseDetail>(p => p.Add(x => x.Id, id));

        cut.Find("[data-field=taker-name]").Change("");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Taker name is required.", cut.Find(".message-error").TextContent));
    }

    [Fact]
    public void Draft_links_to_import_for_its_invoice()
    {
        var invoice = SeptemberInvoice(canada);
        var id = CreateDraft(invoice);

        var cut = Render<NfseDetail>(p => p.Add(x => x.Id, id));

        Assert.EndsWith($"/nfse/import?invoiceId={invoice.Id}", cut.Find("[data-action=import-for-invoice]").GetAttribute("href"));
    }

    [Fact]
    public void Delete_asks_for_confirmation_and_goes_back_to_list()
    {
        ConfirmDialogsReturn(true);
        var id = CreateDraft(SeptemberInvoice(canada));
        var cut = Render<NfseDetail>(p => p.Add(x => x.Id, id));

        cut.Find("[data-action=delete-nfse]").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/nfse", Navigation.Uri));
        Assert.Empty(AllNfses());
    }

    [Fact]
    public void Delete_cancelled_keeps_the_note()
    {
        ConfirmDialogsReturn(false);
        var id = CreateDraft(SeptemberInvoice(canada));
        var cut = Render<NfseDetail>(p => p.Add(x => x.Id, id));

        cut.Find("[data-action=delete-nfse]").Click();

        Assert.Single(AllNfses());
    }

    // ---------- Issued detail ----------

    [Fact]
    public void Issued_note_is_read_only_with_original_xml()
    {
        var id = Import(NfseFixtures.IssuedCanada, SeptemberInvoice(canada));

        var cut = Render<NfseDetail>(p => p.Add(x => x.Id, id));

        Assert.Contains("NFS-e nº 5", cut.Find("h1").TextContent);
        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(cut.FindAll("[data-action=import-for-invoice]"));
        Assert.Contains("R$ 15.207,14", cut.Markup);
        Assert.Contains("<nfse", cut.Find(".xml-source pre").TextContent);
    }

    [Fact]
    public void Unknown_note_shows_not_found()
    {
        var cut = Render<NfseDetail>(p => p.Add(x => x.Id, 999));

        cut.WaitForAssertion(() => Assert.Contains("NFS-e not found.", cut.Markup));
    }

    // ---------- Import ----------

    [Fact]
    public void Import_suggests_the_invoice_and_imports_the_selected_files()
    {
        var canadaInvoice = SeptemberInvoice(canada);
        var ukInvoice = SeptemberInvoice(uk);
        var cut = Render<Import>();

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(NfseFixtures.Read(NfseFixtures.IssuedCanada), "NFSE_5.xml"),
            InputFileContent.CreateFromBinary(NfseFixtures.Read(NfseFixtures.IssuedUk), "NFSE_6.xml"));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".import-row").Count));
        var selects = cut.FindAll(".invoice-select");
        Assert.Equal(canadaInvoice.Id.ToString(), selects[0].GetAttribute("value"));
        Assert.Equal(ukInvoice.Id.ToString(), selects[1].GetAttribute("value"));

        cut.Find("[data-action=import]").Click();

        cut.WaitForAssertion(() => Assert.Contains("2 NFS-e imported.", cut.Find(".message").TextContent));
        var notes = AllNfses();
        Assert.Equal(canadaInvoice.Id, notes.Single(n => n.Number == "5").InvoiceId);
        Assert.Equal(ukInvoice.Id, notes.Single(n => n.Number == "6").InvoiceId);
    }

    [Fact]
    public void Import_uses_the_invoice_from_the_query_and_lets_the_user_change_it()
    {
        var canadaInvoice = SeptemberInvoice(canada);
        var other = SeptemberInvoice(canada, InvoiceStatus.Sent);
        Navigation.NavigateTo($"/nfse/import?invoiceId={other.Id}");
        var cut = Render<Import>();

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(NfseFixtures.Read(NfseFixtures.IssuedCanada), "NFSE_5.xml"));

        cut.WaitForAssertion(() => Assert.Equal(other.Id.ToString(), cut.Find(".invoice-select").GetAttribute("value")));
        cut.Find(".invoice-select").Change(canadaInvoice.Id.ToString());
        cut.Find("[data-action=import]").Click();

        cut.WaitForAssertion(() => Assert.Single(AllNfses()));
        Assert.Equal(canadaInvoice.Id, AllNfses().Single().InvoiceId);
    }

    [Fact]
    public void Import_shows_errors_for_invalid_and_already_imported_files()
    {
        Import(NfseFixtures.IssuedCanada, SeptemberInvoice(canada));
        var cut = Render<Import>();

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(NfseFixtures.Read(NfseFixtures.IssuedCanada), "NFSE_5.xml"),
            InputFileContent.CreateFromText("not xml", "broken.xml"));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".import-row").Count));
        Assert.Contains("Already imported", cut.FindAll(".import-row")[0].TextContent);
        Assert.Contains("Invalid XML", cut.FindAll(".import-row")[1].TextContent);
        Assert.True(cut.Find("[data-action=import]").HasAttribute("disabled"));
    }

    // ---------- Invoice detail ----------

    [Fact]
    public void Invoice_detail_creates_draft_and_opens_it()
    {
        var invoice = SeptemberInvoice(canada, InvoiceStatus.Sent);
        var cut = Render<InvoiceDetail>(p => p.Add(x => x.Id, invoice.Id));

        cut.Find("[data-action=create-nfse]").Click();

        cut.WaitForAssertion(() => Assert.Single(AllNfses()));
        Assert.Contains($"/nfse/{AllNfses().Single().Id}", Navigation.Uri);
    }

    [Fact]
    public void Invoice_detail_lists_linked_notes_and_hides_create_when_issued()
    {
        var invoice = SeptemberInvoice(canada);
        Import(NfseFixtures.IssuedCanada, invoice);

        var cut = Render<InvoiceDetail>(p => p.Add(x => x.Id, invoice.Id));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".nfse-row")));
        Assert.Contains("5", cut.Find(".nfse-row").TextContent);
        Assert.Empty(cut.FindAll("[data-action=create-nfse]"));
        Assert.EndsWith($"/nfse/import?invoiceId={invoice.Id}", cut.Find("[data-action=import-nfse]").GetAttribute("href"));
    }
}
