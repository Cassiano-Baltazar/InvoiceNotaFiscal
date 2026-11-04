using System.Text;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceApp.Tests.Services;

public class NfseFileServiceTests : ServiceTestBase
{
    private readonly string outputBasePath;
    private readonly int invoiceId;

    public NfseFileServiceTests()
    {
        outputBasePath = Database.SeedCompany().OutputBasePath;
        var client = Database.SeedClient("Sample Client Canada Inc.", amount: 4000m, currency: "CAD");
        invoiceId = Database.SeedInvoice(client.Id, InvoiceStatus.Paid, issueYear: 2026, issueMonth: 11).Id;
    }

    private string Folder => Path.Combine(outputBasePath, "2026-11", "NFSe");

    private NfseFileService CreateService() => new(NewContext(), Pdf, NullLogger<NfseFileService>.Instance);

    private int AddNote(NfseStatus status = NfseStatus.Issued, string? xml = null, NfseSource source = NfseSource.NationalApi)
    {
        using var db = NewContext();
        var note = new Nfse
        {
            InvoiceId = invoiceId,
            Status = status,
            Source = source,
            Number = "1",
            TakerName = "SAMPLE CLIENT CANADA INC.",
            Description = "Software development services - October 2026",
            AmountBrl = 15487.82m,
            NationalAccessKey = NationalNfseSamples.AccessKey,
            XmlContent = xml ?? NationalNfseSamples.Issued(),
            FileName = $"NFSe_{NationalNfseSamples.AccessKey}.xml"
        };
        db.Nfses.Add(note);
        db.SaveChanges();
        return note.Id;
    }

    [Fact]
    public async Task Saves_xml_and_pdf_in_the_invoice_month_folder()
    {
        var id = AddNote();

        var result = await CreateService().SaveFilesAsync(id);

        Assert.True(result.Success, result.Message);
        Assert.Empty(result.Warnings);
        var xmlPath = Path.Combine(Folder, $"NFSe_{NationalNfseSamples.AccessKey}.xml");
        Assert.Equal(NationalNfseSamples.Issued(), await File.ReadAllTextAsync(xmlPath));
        Assert.Equal(FakePdfExportService.PdfBytes, await File.ReadAllBytesAsync(Path.ChangeExtension(xmlPath, ".pdf")));
        Assert.Contains("DANFSe", Assert.Single(Pdf.GeneratedHtml));
    }

    [Fact]
    public async Task Saving_again_replaces_the_files()
    {
        var id = AddNote();
        await CreateService().SaveFilesAsync(id);

        var result = await CreateService().SaveFilesAsync(id);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, Directory.GetFiles(Folder).Length);
    }

    [Fact]
    public async Task Pdf_failure_keeps_the_xml_and_warns()
    {
        Pdf.ShouldFail = true;
        var id = AddNote();

        var result = await CreateService().SaveFilesAsync(id);

        Assert.True(result.Success, result.Message);
        Assert.Contains("PDF could not be created", Assert.Single(result.Warnings));
        Assert.True(File.Exists(Path.Combine(Folder, $"NFSe_{NationalNfseSamples.AccessKey}.xml")));
        Assert.False(File.Exists(Path.Combine(Folder, $"NFSe_{NationalNfseSamples.AccessKey}.pdf")));
    }

    [Fact]
    public async Task City_hall_note_saves_only_the_xml()
    {
        var xml = Encoding.Latin1.GetString(NfseFixtures.Read(NfseFixtures.IssuedCanada));
        var id = AddNote(xml: xml, source: NfseSource.Imported);

        var result = await CreateService().SaveFilesAsync(id);

        Assert.True(result.Success, result.Message);
        Assert.Single(Directory.GetFiles(Folder));
        Assert.Empty(Pdf.GeneratedHtml);
    }

    [Fact]
    public async Task Draft_is_not_saved()
    {
        var id = AddNote(NfseStatus.Draft);

        var result = await CreateService().SaveFilesAsync(id);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public async Task Needs_the_output_folder()
    {
        var id = AddNote();
        using (var db = NewContext())
        {
            db.CompanySettings.Single().OutputBasePath = "";
            db.SaveChanges();
        }

        var result = await CreateService().SaveFilesAsync(id);

        Assert.False(result.Success);
        Assert.Contains("output folder", result.Message);
    }
}
