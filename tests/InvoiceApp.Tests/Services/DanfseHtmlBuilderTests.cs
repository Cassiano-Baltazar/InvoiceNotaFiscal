using System.Text;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class DanfseHtmlBuilderTests
{
    [Fact]
    public void Shows_the_main_data_of_the_note()
    {
        var html = DanfseHtmlBuilder.Build(NationalNfseSamples.Issued());

        Assert.Contains(NationalNfseSamples.AccessKey, html);
        Assert.Contains("04/11/2026 10:27:31", html);
        Assert.Contains("31/10/2026", html);
        Assert.Contains("1 / 8", html);
        Assert.Contains("TEST COMPANY LTDA", html);
        Assert.Contains("11.222.333/0001-44", html);
        Assert.Contains("Simples Nacional - ME/EPP", html);
        Assert.Contains("SAMPLE CLIENT CANADA INC.", html);
        Assert.Contains("NIF não exigido", html);
        Assert.Contains("Montreal - QC - CA - H2X 1Y4", html);
        Assert.Contains("Software development services - October 2026", html);
        Assert.Contains("R$ 15.487,82", html);
        Assert.Contains("4.000,00", html);
        Assert.Contains("Exportação de serviço", html);
        Assert.Contains("3,05%", html);
        Assert.Contains("Invoice INV-2026-11-001", html);
        Assert.DoesNotContain("SEM VALOR FISCAL", html);
    }

    [Fact]
    public void Test_note_is_marked_without_fiscal_value()
    {
        Assert.Contains("SEM VALOR FISCAL", DanfseHtmlBuilder.Build(NationalNfseSamples.Issued(environment: "2")));
    }

    [Fact]
    public void Encodes_text_from_the_xml()
    {
        var xml = NationalNfseSamples.Issued().Replace("SAMPLE CLIENT CANADA INC.", "A &amp; B &lt;script&gt;");

        var html = DanfseHtmlBuilder.Build(xml);

        Assert.Contains("A &amp; B &lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Recognizes_the_national_layout()
    {
        Assert.True(DanfseHtmlBuilder.IsNationalNfse(NationalNfseSamples.Issued()));
        Assert.False(DanfseHtmlBuilder.IsNationalNfse(Encoding.Latin1.GetString(NfseFixtures.Read(NfseFixtures.IssuedCanada))));
        Assert.False(DanfseHtmlBuilder.IsNationalNfse("not xml"));
    }
}
