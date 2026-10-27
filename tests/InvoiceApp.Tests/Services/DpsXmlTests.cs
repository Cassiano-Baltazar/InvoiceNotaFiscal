using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class DpsXmlTests
{
    private const string Ns = DpsXml.Namespace;

    private static CompanySettings Company() => new()
    {
        Cnpj = "11.222.333/0001-44",
        MunicipalityCode = "4309308",
        DpsSeries = "1",
        SimplesNacionalTaxRate = 6m,
        NfseEnvironment = NfseEnvironment.RestrictedProduction
    };

    private static Client UkClient() => new()
    {
        Name = "Sample Client UK Ltd",
        AddressLine1 = "100 Example Street",
        City = "London",
        PostalCode = "EC1A 1AA",
        Country = "United Kingdom",
        CountryCode = "GB",
        Currency = "GBP"
    };

    private static Nfse Draft() => new()
    {
        TakerName = "SAMPLE CLIENT UK LTD",
        TakerCountryCode = "GB",
        TakerTaxId = "12345678",
        Description = "Software development services - October 2026",
        Notes = "Invoice INV-2026-11-001 - GBP 1,000.00",
        NationalTaxCode = "010101",
        NbsCode = "115022000",
        AmountBrl = 6858.05m,
        CurrencyCode = "GBP",
        AmountForeign = 1000m,
        ServiceMode = ForeignServiceMode.CrossBorder,
        ResultCountryCode = "GB",
        ServiceDate = new DateOnly(2026, 10, 31)
    };

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 13, 0, 0, TimeSpan.Zero);

    private static XmlDocument Build(Nfse? nfse = null, Client? client = null, long number = 42, CompanySettings? company = null) =>
        DpsXml.Build(new DpsData(nfse ?? Draft(), client ?? UkClient(), company ?? Company(), NfseEnvironment.RestrictedProduction, "1", number, Now));

    private static string Value(XmlDocument document, string name) =>
        document.GetElementsByTagName(name, Ns)[0]?.InnerText ?? throw new Xunit.Sdk.XunitException($"<{name}> not found");

    private static bool Has(XmlDocument document, string name) => document.GetElementsByTagName(name, Ns).Count > 0;

    [Fact]
    public void Id_has_the_official_format()
    {
        var id = DpsXml.BuildId("4309308", "11222333000144", "1", 42);

        Assert.Equal("DPS4309308211222333000144000010000000000000" + "42", id);
        Assert.Equal(45, id.Length);
    }

    [Fact]
    public void Built_dps_matches_the_official_schema()
    {
        Assert.Empty(DpsXml.Validate(Build()));
    }

    [Fact]
    public void Schema_validation_reports_invalid_values()
    {
        var nfse = Draft();
        nfse.NationalTaxCode = "12";

        Assert.Contains(DpsXml.Validate(Build(nfse)), e => e.Contains("cTribNac"));
    }

    [Fact]
    public void Dps_carries_the_export_data()
    {
        var dps = Build();

        Assert.Equal(DpsXml.BuildId("4309308", "11222333000144", "1", 42), ((XmlElement)dps.GetElementsByTagName("infDPS", Ns)[0]!).GetAttribute("Id"));
        Assert.Equal("2", Value(dps, "tpAmb"));
        Assert.Equal("2026-11-03T10:00:00-03:00", Value(dps, "dhEmi"));
        Assert.Equal("2026-10-31", Value(dps, "dCompet"));
        Assert.Equal("11222333000144", Value(dps, "CNPJ"));
        Assert.Equal("3", Value(dps, "opSimpNac"));
        Assert.False(Has(dps, "IM"));
        Assert.Equal("GB", Value(dps, "cPaisPrestacao"));
        Assert.False(Has(dps, "cLocPrestacao"));
        Assert.Equal("12345678", Value(dps, "NIF"));
        Assert.Equal("SAMPLE CLIENT UK LTD", Value(dps, "xNome"));
        Assert.Equal("010101", Value(dps, "cTribNac"));
        Assert.Equal("115022000", Value(dps, "cNBS"));
        Assert.Equal("540", Value(dps, "tpMoeda"));
        Assert.Equal("1000.00", Value(dps, "vServMoeda"));
        Assert.Equal("1", Value(dps, "mdPrestacao"));
        Assert.Equal("0", Value(dps, "vincPrest"));
        Assert.Equal("6858.05", Value(dps, "vServ"));
        Assert.Equal("3", Value(dps, "tribISSQN"));
        Assert.Equal("GB", Value(dps, "cPaisResult"));
        Assert.False(Has(dps, "pAliq"));
        Assert.Equal("6.00", Value(dps, "pTotTribSN"));
        Assert.Equal("Invoice INV-2026-11-001 - GBP 1,000.00", Value(dps, "xInfComp"));
    }

    [Fact]
    public void Municipal_registration_is_sent_when_filled_in()
    {
        var company = Company();
        company.MunicipalRegistration = " 35165 ";

        var dps = Build(company: company);

        Assert.Equal("35165", Value(dps, "IM"));
        Assert.Empty(DpsXml.Validate(dps));
    }

    [Fact]
    public void Service_date_after_the_issue_date_uses_the_issue_date()
    {
        var nfse = Draft();
        nfse.ServiceDate = new DateOnly(2026, 12, 1);

        Assert.Equal("2026-11-03", Value(Build(nfse), "dCompet"));
    }

    [Fact]
    public void Taker_without_tax_id_sends_the_reason()
    {
        var nfse = Draft();
        nfse.TakerTaxId = null;
        nfse.TakerMissingTaxIdReason = MissingTaxIdReason.NotRequired;

        var dps = Build(nfse);

        Assert.False(Has(dps, "NIF"));
        Assert.Equal("2", Value(dps, "cNaoNIF"));
        Assert.Empty(DpsXml.Validate(dps));
    }

    [Theory]
    [InlineData("100 Example Street", "Example Street", "100")]
    [InlineData("Example Road, 25", "Example Road", "25")]
    [InlineData("Example Road", "Example Road", "S/N")]
    public void Foreign_address_splits_street_and_number(string line, string street, string number)
    {
        var client = UkClient();
        client.AddressLine1 = line;
        client.Region = "Greater London";

        var dps = Build(client: client);

        Assert.Equal(street, Value(dps, "xLgr"));
        Assert.Equal(number, Value(dps, "nro"));
        Assert.Equal("London", Value(dps, "xBairro"));
        Assert.Equal("Greater London", Value(dps, "xEstProvReg"));
        Assert.Equal("EC1A 1AA", Value(dps, "cEndPost"));
        Assert.Empty(DpsXml.Validate(dps));
    }

    [Fact]
    public void Address_is_left_out_without_postal_code()
    {
        var client = UkClient();
        client.PostalCode = null;

        var dps = Build(client: client);

        Assert.False(Has(dps, "end"));
        Assert.Empty(DpsXml.Validate(dps));
    }

    [Fact]
    public void Signature_is_valid_and_keeps_the_dps_valid()
    {
        using var certificate = X509CertificateLoader.LoadPkcs12(TestCertificates.Pfx(), TestCertificates.Password, X509KeyStorageFlags.Exportable);
        var dps = Build();

        var signedXml = DpsXml.Sign(dps, certificate);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?><DPS", signedXml);
        var check = new XmlDocument { PreserveWhitespace = true };
        check.LoadXml(signedXml);
        Assert.Empty(DpsXml.Validate(check));
        var signature = (XmlElement)check.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!;
        Assert.Equal("DPS", signature.ParentNode!.LocalName);
        var verifier = new SignedXml(check);
        verifier.LoadXml(signature);
        Assert.True(verifier.CheckSignature(certificate, verifySignatureOnly: true));
        Assert.Equal(SignedXml.XmlDsigRSASHA256Url, verifier.SignatureMethod);
        Assert.Equal("#" + DpsXml.BuildId("4309308", "11222333000144", "1", 42), ((Reference)verifier.SignedInfo!.References[0]!).Uri);
    }

    [Fact]
    public void Check_lists_what_is_missing()
    {
        var nfse = Draft();
        nfse.AmountBrl = 0;
        nfse.CurrencyCode = "XYZ";
        nfse.TakerTaxId = null;
        nfse.ResultCountryCode = null;
        var company = Company();
        company.MunicipalityCode = "";
        company.SimplesNacionalTaxRate = 0;

        var problems = DpsXml.Check(nfse, company);

        Assert.Contains(problems, p => p.Contains("municipality code"));
        Assert.Contains(problems, p => p.Contains("Simples Nacional tax rate"));
        Assert.Contains(problems, p => p.Contains("Amount (BRL)"));
        Assert.Contains(problems, p => p.Contains("Currency 'XYZ'"));
        Assert.Contains(problems, p => p.Contains("tax id (NIF)"));
        Assert.Contains(problems, p => p.Contains("result is used"));
        Assert.Empty(DpsXml.Check(Draft(), Company()));
    }

    [Fact]
    public void Reads_number_and_time_of_the_issued_note()
    {
        var (number, processedAt) = DpsXml.ReadIssued(FakeNationalNfseApi.IssuedXml("15"));

        Assert.Equal("15", number);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 15, 0), processedAt);
    }
}
