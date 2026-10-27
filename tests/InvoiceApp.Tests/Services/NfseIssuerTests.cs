using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Xml;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceApp.Tests.Services;

public class NfseIssuerTests : ServiceTestBase
{
    private readonly FakeNationalNfseApi api = new();
    private readonly AesSecretProtector protector = new(RandomNumberGenerator.GetBytes(32));
    private readonly int draftId;
    private readonly int invoiceId;

    public NfseIssuerTests()
    {
        Database.SeedCompany(c =>
        {
            c.Cnpj = NfseFixtures.CompanyCnpj;
            c.MunicipalityCode = "4309308";
            c.SimplesNacionalTaxRate = 6m;
            c.NfseEnvironment = NfseEnvironment.Production;
            c.NextDpsNumber = 10;
        });
        var client = Database.SeedClient("Sample Client UK Ltd", amount: 1000m, currency: "GBP");
        using (var db = NewContext())
        {
            var saved = db.Clients.Single();
            saved.CountryCode = "GB";
            saved.TaxId = "12345678";
            db.SaveChanges();
        }

        invoiceId = Database.SeedInvoice(client.Id, InvoiceStatus.Paid).Id;
        draftId = CreateNfseService().CreateDraftForInvoiceAsync(invoiceId).GetAwaiter().GetResult().NfseId!.Value;
        using (var db = NewContext())
        {
            db.Nfses.Single().AmountBrl = 6858.05m;
            db.SaveChanges();
        }

        UploadCertificate();
    }

    private void UploadCertificate(string holder = "TEST COMPANY LTDA:11222333000144") =>
        new CertificateService(NewContext(), protector)
            .SaveAsync(TestCertificates.Pfx(holder), "a.pfx", TestCertificates.Password).GetAwaiter().GetResult();

    private NfseIssuer CreateIssuer() =>
        new(NewContext(), new CertificateService(NewContext(), protector), api, TimeProvider.System, NullLogger<NfseIssuer>.Instance);

    private Nfse Note()
    {
        using var db = NewContext();
        return db.Nfses.AsNoTracking().Single(x => x.Id == draftId);
    }

    private long NextNumber()
    {
        using var db = NewContext();
        return db.CompanySettings.AsNoTracking().Single().NextDpsNumber;
    }

    private void SetEnvironment(NfseEnvironment environment)
    {
        using var db = NewContext();
        db.CompanySettings.Single().NfseEnvironment = environment;
        db.SaveChanges();
    }

    [Fact]
    public async Task Issues_the_draft_in_production()
    {
        var result = await CreateIssuer().IssueAsync(draftId);

        Assert.True(result.Success, result.Message);
        Assert.Contains("NFS-e nº 7 issued", result.Message);
        var note = Note();
        Assert.Equal(NfseStatus.Issued, note.Status);
        Assert.Equal(NfseSource.NationalApi, note.Source);
        Assert.Equal("7", note.Number);
        Assert.Equal(FakeNationalNfseApi.AccessKey, note.NationalAccessKey);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 15, 0), note.IssuedAt);
        Assert.Equal(FakeNationalNfseApi.IssuedXml(), note.XmlContent);
        Assert.Equal(10, note.DpsNumber);
        Assert.Equal(DpsXml.BuildId("4309308", "11222333000144", "1", 10), note.DpsId);
        Assert.Null(note.IssueError);
        Assert.Equal(11, NextNumber());
    }

    [Fact]
    public async Task Sends_a_signed_dps_that_matches_the_schema()
    {
        await CreateIssuer().IssueAsync(draftId);

        var (environment, xml) = Assert.Single(api.Sent);
        Assert.Equal(NfseEnvironment.Production, environment);
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);
        Assert.Empty(DpsXml.Validate(document));
        Assert.Equal("1", document.GetElementsByTagName("tpAmb", DpsXml.Namespace)[0]!.InnerText);
        Assert.Equal("540", document.GetElementsByTagName("tpMoeda", DpsXml.Namespace)[0]!.InnerText);
        var signature = new SignedXml(document);
        signature.LoadXml((XmlElement)document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!);
        Assert.True(signature.CheckSignature());
    }

    [Fact]
    public async Task Test_environment_keeps_the_draft_and_frees_the_number()
    {
        SetEnvironment(NfseEnvironment.RestrictedProduction);

        var result = await CreateIssuer().IssueAsync(draftId);

        Assert.True(result.Success, result.Message);
        Assert.Contains("Test NFS-e nº 7 accepted", result.Message);
        Assert.Equal(NfseEnvironment.RestrictedProduction, api.Sent.Single().Environment);
        var note = Note();
        Assert.Equal(NfseStatus.Draft, note.Status);
        Assert.Null(note.DpsId);
        Assert.Null(note.NationalAccessKey);
        Assert.Equal(11, NextNumber());
    }

    [Fact]
    public async Task Rejection_is_saved_and_the_retry_reuses_the_same_dps()
    {
        api.NextResult = FakeNationalNfseApi.Rejection("E0712", "Total de tributos");

        var first = await CreateIssuer().IssueAsync(draftId);

        Assert.False(first.Success);
        Assert.Contains("E0712: Total de tributos", first.Message);
        var rejected = Note();
        Assert.Equal(NfseStatus.Draft, rejected.Status);
        Assert.Contains("E0712", rejected.IssueError);

        var second = await CreateIssuer().IssueAsync(draftId);

        Assert.True(second.Success, second.Message);
        Assert.Equal([rejected.DpsId!], api.Lookups);
        Assert.Equal(2, api.Sent.Count);
        Assert.Equal(rejected.DpsId, Note().DpsId);
        Assert.Equal(11, NextNumber());
    }

    [Fact]
    public async Task Lost_answer_is_recovered_without_issuing_again()
    {
        api.NextException = new HttpRequestException("timeout");
        var first = await CreateIssuer().IssueAsync(draftId);
        Assert.False(first.Success);
        Assert.Contains("Try again", first.Message);

        api.KeyForLookup = FakeNationalNfseApi.AccessKey;
        var second = await CreateIssuer().IssueAsync(draftId);

        Assert.True(second.Success, second.Message);
        Assert.Single(api.Sent);
        var note = Note();
        Assert.Equal(NfseStatus.Issued, note.Status);
        Assert.Equal("8", note.Number);
    }

    [Fact]
    public async Task Missing_data_is_reported_before_reserving_a_number()
    {
        using (var db = NewContext())
        {
            var note = db.Nfses.Single();
            note.AmountBrl = 0;
            note.TakerTaxId = null;
            db.SaveChanges();
        }

        var result = await CreateIssuer().IssueAsync(draftId);

        Assert.False(result.Success);
        Assert.Contains("Amount (BRL)", result.Message);
        Assert.Contains("tax id (NIF)", result.Message);
        Assert.Empty(api.Sent);
        Assert.Null(Note().DpsId);
        Assert.Equal(10, NextNumber());
    }

    [Fact]
    public async Task Certificate_of_another_company_is_refused()
    {
        UploadCertificate("OTHER COMPANY LTDA:99888777000166");

        var result = await CreateIssuer().IssueAsync(draftId);

        Assert.False(result.Success);
        Assert.Contains("not from the company CNPJ", result.Message);
        Assert.Empty(api.Sent);
    }

    [Fact]
    public async Task Without_certificate_nothing_is_sent()
    {
        await new CertificateService(NewContext(), protector).RemoveAsync();

        var result = await CreateIssuer().IssueAsync(draftId);

        Assert.False(result.Success);
        Assert.Contains("Upload the digital certificate", result.Message);
        Assert.Empty(api.Sent);
    }

    [Fact]
    public async Task Issued_note_cannot_be_issued_again()
    {
        await CreateIssuer().IssueAsync(draftId);

        var result = await CreateIssuer().IssueAsync(draftId);

        Assert.False(result.Success);
        Assert.Single(api.Sent);
    }

    [Fact]
    public async Task Draft_gets_the_national_tax_code_from_settings()
    {
        Assert.Equal("010101", Note().NationalTaxCode);
    }
}
