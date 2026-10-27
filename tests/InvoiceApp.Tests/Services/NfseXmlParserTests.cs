using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class NfseXmlParserTests
{
    [Fact]
    public void Reads_an_issued_note_from_the_portal()
    {
        var data = NfseXmlParser.Parse(NfseFixtures.Read(NfseFixtures.IssuedCanada));

        Assert.Equal("5", data.Number);
        Assert.Equal("1", data.Series);
        Assert.Equal(new DateTime(2026, 10, 8, 17, 42, 46), data.IssuedAt);
        Assert.Equal(new DateOnly(2026, 10, 8), data.ServiceDate);
        Assert.Equal(1, data.MunicipalStatusCode);
        Assert.Equal("Emitida", data.MunicipalStatusDescription);
        Assert.Equal(NfseStatus.Issued, data.Status);
        Assert.Equal("11222333000144", data.ProviderCnpj);
        Assert.Equal("SAMPLE CLIENT CANADA INC.", data.TakerName);
        Assert.Equal("CA", data.TakerCountryCode);
        Assert.Equal("Software development services - September 2026", data.Description);
        Assert.Equal("620230000", data.ActivityCode);
        Assert.Equal("10101", data.ServiceListItem);
        Assert.Equal("115022000", data.NbsCode);
        Assert.Equal("14", data.TaxSituation);
        Assert.Equal(2.17m, data.IssRate);
        Assert.Equal(15207.14m, data.AmountBrl);
        Assert.StartsWith("https://nfse-example.atende.net/", data.Link);
        Assert.NotNull(data.NationalAccessKey);
        Assert.NotNull(data.VerificationCode);
        Assert.Contains("<numero_nfse>5</numero_nfse>", data.XmlContent);
    }

    [Fact]
    public void Decodes_iso_8859_1_accents()
    {
        var data = NfseXmlParser.Parse(NfseFixtures.Read(NfseFixtures.IssuedCanada));

        Assert.Equal("Canadá", data.TakerCountry);
        Assert.Equal("Nota referente a serviço prestado no mes de agosto de 2026", data.Notes);
    }

    [Fact]
    public void Withheld_taxes_are_zero_for_exported_services()
    {
        var data = NfseXmlParser.Parse(NfseFixtures.Read(NfseFixtures.IssuedUk));

        Assert.Equal(0m, data.IrAmount + data.InssAmount + data.CsllAmount + data.PisAmount + data.CofinsAmount + data.IssWithheldAmount);
        Assert.Equal(0m, data.Discount);
    }

    [Fact]
    public void Reads_a_replaced_note_and_ignores_empty_observation()
    {
        var data = NfseXmlParser.Parse(NfseFixtures.Read(NfseFixtures.ReplacedUk));

        Assert.Equal("1", data.Number);
        Assert.Equal(3, data.MunicipalStatusCode);
        Assert.Equal("Substituída", data.MunicipalStatusDescription);
        Assert.Equal(NfseStatus.Replaced, data.Status);
        Assert.Equal(6858.05m, data.AmountBrl);
        Assert.Null(data.Notes);
    }

    [Theory]
    [InlineData("1", NfseStatus.Issued)]
    [InlineData("2", NfseStatus.Cancelled)]
    [InlineData("3", NfseStatus.Replaced)]
    public void Maps_municipal_status(string code, NfseStatus expected)
    {
        var data = NfseXmlParser.Parse(NfseFixtures.Xml(
            $"<nfse><nf><numero_nfse>9</numero_nfse><situacao_codigo_nfse>{code}</situacao_codigo_nfse></nf></nfse>"));

        Assert.Equal(expected, data.Status);
    }

    [Fact]
    public void Note_without_number_is_a_draft_and_unknown_date_is_ignored()
    {
        // Preview file produced before issuing: no number and the "empty" date 30/12/1899.
        var data = NfseXmlParser.Parse(NfseFixtures.Xml(
            "<nfse Id=\"1\"><nf><data_fato_gerador>14/10/2026</data_fato_gerador><valor_total>1,00</valor_total>" +
            "<numero_nfse/><data_nfse>30/12/1899</data_nfse><hora_nfse>00:00:00</hora_nfse></nf>" +
            "<tomador><nome_razao_social>X</nome_razao_social></tomador></nfse>"));

        Assert.Null(data.Number);
        Assert.Equal(NfseStatus.Draft, data.Status);
        Assert.Null(data.IssuedAt);
        Assert.Equal(new DateOnly(2026, 10, 14), data.ServiceDate);
        Assert.Equal(1m, data.AmountBrl);
    }

    [Fact]
    public void Several_items_are_joined_and_withheld_iss_is_summed()
    {
        var data = NfseXmlParser.Parse(NfseFixtures.Xml(
            "<nfse><nf><numero_nfse>7</numero_nfse><situacao_codigo_nfse>1</situacao_codigo_nfse></nf><itens>" +
            "<lista><descritivo>First</descritivo><valor_issrf>1,50</valor_issrf></lista>" +
            "<lista><descritivo>Second</descritivo><valor_issrf>2,25</valor_issrf></lista>" +
            "</itens></nfse>"));

        Assert.Equal($"First{Environment.NewLine}Second", data.Description);
        Assert.Equal(3.75m, data.IssWithheldAmount);
    }

    [Theory]
    [InlineData("<html></html>", "not an NFS-e")]
    [InlineData("<nfse><x/></nfse>", "missing <nf>")]
    [InlineData("<nfse><nf>", "Invalid XML")]
    public void Rejects_files_that_are_not_nfse(string body, string expectedMessage)
    {
        var ex = Assert.Throws<FormatException>(() => NfseXmlParser.Parse(NfseFixtures.Xml(body)));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("15.207,14", 15207.14)]
    [InlineData("2,0100", 2.01)]
    [InlineData("0,00", 0)]
    [InlineData("1.234.567,89", 1234567.89)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("abc", 0)]
    public void Parses_brazilian_numbers(string? value, double expected) =>
        Assert.Equal((decimal)expected, NfseXmlParser.ParseDecimal(value));

    [Fact]
    public void Parses_xml_given_as_string()
    {
        var data = NfseXmlParser.Parse("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><nfse><nf><numero_nfse>3</numero_nfse></nf></nfse>");

        Assert.Equal("3", data.Number);
    }
}
