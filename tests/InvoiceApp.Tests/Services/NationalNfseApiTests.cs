using System.Net;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class NationalNfseApiTests
{
    [Fact]
    public void Success_returns_the_key_and_the_unzipped_nfse()
    {
        var body = $$"""{"tipoAmbiente":2,"chaveAcesso":"KEY","idDps":"DPS1","nfseXmlGZipB64":"{{HttpNationalNfseApi.GZipBase64Encode("<NFSe/>")}}","alertas":[{"codigo":"A1","descricao":"Note"}]}""";

        var result = HttpNationalNfseApi.ParseResult(HttpStatusCode.Created, body);

        Assert.True(result.Issued);
        Assert.Equal("KEY", result.AccessKey);
        Assert.Equal("<NFSe/>", result.NfseXml);
        Assert.Equal("A1: Note", Assert.Single(result.Alerts).ToString());
    }

    [Fact]
    public void Rejection_returns_every_message()
    {
        const string body = """{"erros":[{"codigo":"E0712","descricao":"Total de tributos"},{"Codigo":"E0100","Descricao":"Outro","Complemento":"x"}]}""";

        var result = HttpNationalNfseApi.ParseResult(HttpStatusCode.BadRequest, body);

        Assert.False(result.Issued);
        Assert.Equal(["E0712: Total de tributos", "E0100: Outro (x)"], result.Errors.Select(x => x.ToString()));
    }

    [Fact]
    public void Single_error_object_is_read_too()
    {
        var result = HttpNationalNfseApi.ParseResult(HttpStatusCode.BadRequest, """{"erro":{"codigo":"E1","descricao":"D"}}""");

        Assert.Equal("E1: D", Assert.Single(result.Errors).ToString());
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("{}")]
    public void Unknown_answer_is_an_http_error(string body)
    {
        var ex = Assert.Throws<HttpRequestException>(() => HttpNationalNfseApi.ParseResult(HttpStatusCode.BadGateway, body));

        Assert.Contains("HTTP 502", ex.Message);
    }

    [Fact]
    public void Gzip_round_trip_keeps_accents()
    {
        Assert.Equal("Guaíba", HttpNationalNfseApi.GZipBase64Decode(HttpNationalNfseApi.GZipBase64Encode("Guaíba")));
    }
}
