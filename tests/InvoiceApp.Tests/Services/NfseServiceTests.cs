using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using InvoiceApp.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Tests.Services;

public class NfseServiceTests : ServiceTestBase
{
    private readonly Client canada;
    private readonly Client uk;

    public NfseServiceTests()
    {
        Database.SeedCompany(c => c.Cnpj = NfseFixtures.CompanyCnpj);
        canada = Database.SeedClient("Sample Client Canada Inc.", amount: 4000m, currency: "CAD");
        uk = Database.SeedClient("Sample Client UK Ltd", amount: 1000m, currency: "GBP");
    }

    // Invoices issued in October bill September (the service month of the fixtures).
    private Invoice SeptemberInvoice(Client client, InvoiceStatus status = InvoiceStatus.Paid) =>
        Database.SeedInvoice(client.Id, status, issueYear: 2026, issueMonth: 10);

    private Invoice AugustInvoice(Client client, InvoiceStatus status = InvoiceStatus.Paid) =>
        Database.SeedInvoice(client.Id, status, issueYear: 2026, issueMonth: 9);

    private List<Nfse> AllNfses()
    {
        using var db = Database.CreateContext();
        return db.Nfses.AsNoTracking().OrderBy(x => x.Id).ToList();
    }

    // ---------- Drafts ----------

    [Fact]
    public async Task Creates_draft_from_invoice_with_default_fiscal_codes()
    {
        var invoice = SeptemberInvoice(canada);

        var result = await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id);

        Assert.True(result.Success);
        var draft = Assert.Single(AllNfses());
        Assert.Equal(result.NfseId, draft.Id);
        Assert.Equal(invoice.Id, draft.InvoiceId);
        Assert.Equal(NfseStatus.Draft, draft.Status);
        Assert.Equal(NfseSource.Manual, draft.Source);
        Assert.Null(draft.Number);
        Assert.Equal("SAMPLE CLIENT CANADA INC.", draft.TakerName);
        Assert.Equal("Canada", draft.TakerCountry);
        Assert.Equal("Services", draft.Description);
        Assert.Equal($"Invoice {invoice.InvoiceNumber} - CAD 4,000.00", draft.Notes);
        Assert.Equal("620230000", draft.ActivityCode);
        Assert.Equal("10101", draft.ServiceListItem);
        Assert.Equal("115022000", draft.NbsCode);
        Assert.Equal("14", draft.TaxSituation);
        Assert.Equal(2.01m, draft.IssRate);
        Assert.Equal(0m, draft.AmountBrl);
    }

    [Fact]
    public async Task Draft_description_uses_the_invoice_items_with_plain_dashes()
    {
        var invoice = SeptemberInvoice(canada);
        using (var db = Database.CreateContext())
        {
            var item = db.InvoiceItems.Single(x => x.InvoiceId == invoice.Id);
            item.Description = "Software development services — September 2026";
            db.InvoiceItems.Add(new InvoiceItem { InvoiceId = invoice.Id, Position = 2, Description = "Extra hours", Quantity = 2, UnitPrice = 50, Amount = 100 });
            db.SaveChanges();
        }

        await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id);

        Assert.Equal("Software development services - September 2026; Extra hours", Assert.Single(AllNfses()).Description);
    }

    [Fact]
    public async Task Draft_copies_taker_and_codes_from_the_last_note_of_the_same_client()
    {
        var august = AugustInvoice(canada);
        var importResult = await CreateNfseService().ImportAsync("a.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada), august.Id);
        Assert.True(importResult.Success);
        var september = SeptemberInvoice(canada);

        await CreateNfseService().CreateDraftForInvoiceAsync(september.Id);

        var draft = AllNfses().Single(x => x.Status == NfseStatus.Draft);
        Assert.Equal("SAMPLE CLIENT CANADA INC.", draft.TakerName);
        Assert.Equal("Canadá", draft.TakerCountry);
        Assert.Equal("CA", draft.TakerCountryCode);
        Assert.Equal(2.17m, draft.IssRate);
    }

    [Fact]
    public async Task Does_not_create_a_second_draft_or_a_draft_next_to_an_issued_note()
    {
        var invoice = SeptemberInvoice(canada);
        var service = CreateNfseService();
        var first = await service.CreateDraftForInvoiceAsync(invoice.Id);

        var second = await service.CreateDraftForInvoiceAsync(invoice.Id);

        Assert.True(second.Success);
        Assert.Equal(first.NfseId, second.NfseId);
        Assert.Contains("already has an NFS-e draft", second.Message);
        Assert.Single(AllNfses());

        var other = SeptemberInvoice(uk);
        await CreateNfseService().ImportAsync("uk.xml", NfseFixtures.Read(NfseFixtures.IssuedUk), other.Id);
        var third = await CreateNfseService().CreateDraftForInvoiceAsync(other.Id);
        Assert.Contains("already has NFS-e nº 6", third.Message);
        Assert.Equal(2, AllNfses().Count);
    }

    [Fact]
    public async Task Cancelled_or_missing_invoice_cannot_get_a_draft()
    {
        var cancelled = SeptemberInvoice(canada, InvoiceStatus.Cancelled);
        var service = CreateNfseService();

        Assert.False((await service.CreateDraftForInvoiceAsync(cancelled.Id)).Success);
        Assert.False((await service.CreateDraftForInvoiceAsync(999)).Success);
        Assert.Empty(AllNfses());
    }

    private static NfseEditModel ValidDraft() => new()
    {
        TakerName = " SAMPLE CLIENT CANADA INC. ",
        TakerCountry = "Canadá",
        TakerCountryCode = "ca",
        ServiceDate = new DateOnly(2026, 10, 8),
        Description = " Software development services - September 2026 ",
        Notes = "  ",
        ActivityCode = "620230000",
        ServiceListItem = "10101",
        NbsCode = "115022000",
        TaxSituation = "14",
        IssRate = 2.17m,
        AmountBrl = 15207.144m
    };

    [Fact]
    public async Task Saves_draft_fields()
    {
        var invoice = SeptemberInvoice(canada);
        var draftId = (await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id)).NfseId!.Value;

        var result = await CreateNfseService().UpdateDraftAsync(draftId, ValidDraft());

        Assert.True(result.Success);
        var draft = Assert.Single(AllNfses());
        Assert.Equal("SAMPLE CLIENT CANADA INC.", draft.TakerName);
        Assert.Equal("CA", draft.TakerCountryCode);
        Assert.Equal("Software development services - September 2026", draft.Description);
        Assert.Null(draft.Notes);
        Assert.Equal(15207.14m, draft.AmountBrl);
        Assert.Equal(2.17m, draft.IssRate);
        Assert.Equal(new DateOnly(2026, 10, 8), draft.ServiceDate);
    }

    public static TheoryData<string, Action<NfseEditModel>> InvalidDrafts => new()
    {
        { "Taker name", m => m.TakerName = "" },
        { "Service description", m => m.Description = " " },
        { "Amount in BRL", m => m.AmountBrl = -1 },
        { "ISS rate", m => m.IssRate = 101 },
        { "Tax amounts", m => m.PisAmount = -0.01m }
    };

    [Theory]
    [MemberData(nameof(InvalidDrafts))]
    public async Task Rejects_invalid_draft(string expectedStart, Action<NfseEditModel> breakModel)
    {
        var invoice = SeptemberInvoice(canada);
        var draftId = (await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id)).NfseId!.Value;
        var model = ValidDraft();
        breakModel(model);

        var result = await CreateNfseService().UpdateDraftAsync(draftId, model);

        Assert.False(result.Success);
        Assert.StartsWith(expectedStart, result.Message);
    }

    [Fact]
    public async Task Imported_note_cannot_be_edited()
    {
        var invoice = SeptemberInvoice(canada);
        var imported = await CreateNfseService().ImportAsync("ca.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada), invoice.Id);

        var result = await CreateNfseService().UpdateDraftAsync(imported.NfseId!.Value, ValidDraft());

        Assert.False(result.Success);
        Assert.Contains("Only drafts", result.Message);
    }

    [Fact]
    public async Task Deletes_note()
    {
        var invoice = SeptemberInvoice(canada);
        var draftId = (await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id)).NfseId!.Value;

        Assert.True((await CreateNfseService().DeleteAsync(draftId)).Success);
        Assert.Empty(AllNfses());
        Assert.False((await CreateNfseService().DeleteAsync(draftId)).Success);
    }

    // ---------- Import ----------

    [Fact]
    public async Task Imports_xml_and_links_it_to_the_chosen_invoice()
    {
        var invoice = SeptemberInvoice(canada);

        var result = await CreateNfseService().ImportAsync("NFSE_5.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada), invoice.Id);

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
        Assert.Equal($"NFS-e nº 5 imported and linked to invoice {invoice.InvoiceNumber}.", result.Message);
        var nfse = Assert.Single(AllNfses());
        Assert.Equal(invoice.Id, nfse.InvoiceId);
        Assert.Equal(NfseStatus.Issued, nfse.Status);
        Assert.Equal(NfseSource.Imported, nfse.Source);
        Assert.Equal("5", nfse.Number);
        Assert.Equal(15207.14m, nfse.AmountBrl);
        Assert.Equal("NFSE_5.xml", nfse.FileName);
        Assert.Contains("<numero_nfse>5</numero_nfse>", nfse.XmlContent);
    }

    [Fact]
    public async Task Issued_note_completes_the_draft_of_the_same_invoice()
    {
        var invoice = SeptemberInvoice(canada);
        var draftId = (await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id)).NfseId;

        var result = await CreateNfseService().ImportAsync("ca.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada), invoice.Id);

        Assert.True(result.Success);
        Assert.Contains("replaced the draft", result.Message);
        var nfse = Assert.Single(AllNfses());
        Assert.Equal(draftId, nfse.Id);
        Assert.Equal(NfseStatus.Issued, nfse.Status);
        Assert.Equal(NfseSource.Imported, nfse.Source);
        Assert.Equal("5", nfse.Number);
    }

    [Fact]
    public async Task Same_xml_cannot_be_imported_twice()
    {
        var invoice = SeptemberInvoice(canada);
        var other = AugustInvoice(canada);
        var xml = NfseFixtures.Read(NfseFixtures.IssuedCanada);
        await CreateNfseService().ImportAsync("ca.xml", xml, invoice.Id);

        var result = await CreateNfseService().ImportAsync("copy.xml", xml, other.Id);

        Assert.False(result.Success);
        Assert.Contains("already imported", result.Message);
        Assert.Contains(invoice.InvoiceNumber, result.Message);
        Assert.Single(AllNfses());
    }

    [Fact]
    public async Task Invoice_cannot_have_two_issued_notes()
    {
        var invoice = SeptemberInvoice(uk);
        await CreateNfseService().ImportAsync("uk.xml", NfseFixtures.Read(NfseFixtures.IssuedUk), invoice.Id);

        var result = await CreateNfseService().ImportAsync("ca.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada), invoice.Id);

        Assert.False(result.Success);
        Assert.Contains("already has the issued NFS-e nº 6", result.Message);
    }

    [Fact]
    public async Task Replaced_note_can_be_kept_next_to_the_issued_one()
    {
        var invoice = SeptemberInvoice(uk);
        await CreateNfseService().ImportAsync("uk.xml", NfseFixtures.Read(NfseFixtures.IssuedUk), invoice.Id);

        var result = await CreateNfseService().ImportAsync("old.xml", NfseFixtures.Read(NfseFixtures.ReplacedUk), invoice.Id);

        Assert.True(result.Success);
        var notes = AllNfses();
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Number == "6" && n.Status == NfseStatus.Issued);
        Assert.Contains(notes, n => n.Number == "1" && n.Status == NfseStatus.Replaced);
    }

    [Fact]
    public async Task Rejects_cancelled_invoice_missing_invoice_invalid_and_unnumbered_xml()
    {
        var cancelled = SeptemberInvoice(canada, InvoiceStatus.Cancelled);
        var service = CreateNfseService();
        var xml = NfseFixtures.Read(NfseFixtures.IssuedCanada);

        Assert.Contains("is cancelled", (await service.ImportAsync("a.xml", xml, cancelled.Id)).Message);
        Assert.Contains("invoice not found", (await service.ImportAsync("a.xml", xml, 999)).Message);
        Assert.Contains("Invalid XML", (await service.ImportAsync("bad.xml", "not xml"u8.ToArray(), cancelled.Id)).Message);
        var unnumbered = NfseFixtures.Xml("<nfse><nf><numero_nfse/></nf></nfse>");
        Assert.Contains("no NFS-e number", (await service.ImportAsync("draft.xml", unnumbered, cancelled.Id)).Message);
        Assert.Empty(AllNfses());
    }

    [Fact]
    public async Task Warns_when_the_provider_cnpj_is_not_the_company_cnpj()
    {
        using (var db = Database.CreateContext())
        {
            db.CompanySettings.Single().Cnpj = "99.888.777/0001-66";
            db.SaveChanges();
        }
        var invoice = SeptemberInvoice(canada);

        var result = await CreateNfseService().ImportAsync("ca.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada), invoice.Id);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("not the company CNPJ"));
    }

    [Fact]
    public async Task No_cnpj_warning_while_settings_still_have_the_placeholder()
    {
        using (var db = Database.CreateContext())
        {
            db.CompanySettings.Single().Cnpj = "00.000.000/0001-00";
            db.SaveChanges();
        }
        var invoice = SeptemberInvoice(canada);

        var result = await CreateNfseService().ImportAsync("ca.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada), invoice.Id);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("not the company CNPJ"));
    }

    // ---------- Preview and suggestion ----------

    [Fact]
    public async Task Preview_suggests_the_invoice_of_the_same_client_and_service_month()
    {
        var august = AugustInvoice(canada);
        var september = SeptemberInvoice(canada);
        SeptemberInvoice(uk);

        var preview = await CreateNfseService().PreviewImportAsync("ca.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada));

        Assert.True(preview.CanImport);
        Assert.Equal("5", preview.Data!.Number);
        Assert.Equal(september.Id, preview.SuggestedInvoiceId);
        Assert.NotEqual(august.Id, preview.SuggestedInvoiceId);
    }

    [Fact]
    public async Task Preview_does_not_mix_clients_whose_names_start_with_the_same_word()
    {
        // Regression: "Sample Client UK Ltd" was suggested the invoice of "Sample Client Canada Inc.".
        SeptemberInvoice(canada);
        var ukInvoice = SeptemberInvoice(uk);

        var preview = await CreateNfseService().PreviewImportAsync("uk.xml", NfseFixtures.Read(NfseFixtures.IssuedUk));

        Assert.Equal(ukInvoice.Id, preview.SuggestedInvoiceId);
    }

    [Fact]
    public async Task Preview_has_no_suggestion_for_an_unknown_client()
    {
        SeptemberInvoice(uk);
        using (var db = Database.CreateContext())
        {
            db.Invoices.Single().ClientName = "Someone Else";
            db.SaveChanges();
        }

        var preview = await CreateNfseService().PreviewImportAsync("ca.xml", NfseFixtures.Read(NfseFixtures.IssuedCanada));

        Assert.Null(preview.SuggestedInvoiceId);
    }

    [Fact]
    public async Task Preview_flags_files_already_imported_and_invalid_files()
    {
        var invoice = SeptemberInvoice(canada);
        var xml = NfseFixtures.Read(NfseFixtures.IssuedCanada);
        await CreateNfseService().ImportAsync("ca.xml", xml, invoice.Id);

        var duplicate = await CreateNfseService().PreviewImportAsync("ca.xml", xml);
        var invalid = await CreateNfseService().PreviewImportAsync("x.txt", "hello"u8.ToArray());

        Assert.False(duplicate.CanImport);
        Assert.Equal(invoice.Id, duplicate.AlreadyImported!.InvoiceId);
        Assert.False(invalid.CanImport);
        Assert.NotNull(invalid.Error);
    }

    [Theory]
    [InlineData("Software development services - September 2026", 2026, 9)]
    [InlineData("Nota referente a serviço prestado no mes de agosto de 2026", 2026, 8)]
    [InlineData("Serviços de março/2027", 2027, 3)]
    public void Finds_service_month_in_english_and_portuguese(string text, int year, int month) =>
        Assert.Equal((year, month), NfseService.FindMonths(text).First());

    [Fact]
    public void Ignores_text_without_month() =>
        Assert.Empty(NfseService.FindMonths("Software development services"));

    // ---------- Queries ----------

    [Fact]
    public async Task Lists_drafts_first_and_filters_by_status()
    {
        var a = SeptemberInvoice(uk);
        var b = SeptemberInvoice(canada);
        await CreateNfseService().ImportAsync("uk.xml", NfseFixtures.Read(NfseFixtures.IssuedUk), a.Id);
        await CreateNfseService().ImportAsync("old.xml", NfseFixtures.Read(NfseFixtures.ReplacedUk), a.Id);
        await CreateNfseService().CreateDraftForInvoiceAsync(b.Id);

        var all = await CreateNfseService().GetAllAsync();
        var issued = await CreateNfseService().GetAllAsync(NfseStatus.Issued);
        var forInvoice = await CreateNfseService().GetForInvoiceAsync(a.Id);

        Assert.Equal(NfseStatus.Draft, all[0].Status);
        Assert.Equal(3, all.Count);
        Assert.All(all, n => Assert.NotNull(n.Invoice));
        Assert.Equal("6", Assert.Single(issued).Number);
        Assert.Equal(["6", "1"], forInvoice.Select(x => x.Number));
    }

    [Fact]
    public async Task Invoice_options_exclude_cancelled_invoices()
    {
        SeptemberInvoice(canada);
        SeptemberInvoice(uk, InvoiceStatus.Cancelled);

        var options = await CreateNfseService().GetInvoiceOptionsAsync();

        var option = Assert.Single(options);
        Assert.Contains("Sample Client Canada Inc.", option.Label);
        Assert.Contains("Sep 2026", option.Label);
        Assert.Contains("4,000.00 CAD", option.Label);
    }
}
