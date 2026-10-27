using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using InvoiceApp.Web.Data.Entities;

namespace InvoiceApp.Web.Services;

public record NationalNfseMessage(string Code, string Description, string? Complement)
{
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Complement) ? $"{Code}: {Description}" : $"{Code}: {Description} ({Complement})";
}

/// <summary>Answer to a DPS: the access key and XML of the NFS-e when it was issued, or the rejection messages.</summary>
public record NationalNfseResult(string? AccessKey, string? NfseXml, IReadOnlyList<NationalNfseMessage> Errors, IReadOnlyList<NationalNfseMessage> Alerts)
{
    public bool Issued => AccessKey is not null && NfseXml is not null;
}

/// <summary>
/// The national NFS-e API (Sefin Nacional). Every call authenticates with the company certificate (mutual TLS).
/// </summary>
public interface INationalNfseApi
{
    Task<NationalNfseResult> SendDpsAsync(NfseEnvironment environment, X509Certificate2 certificate, string signedDpsXml, CancellationToken cancellationToken = default);

    /// <summary>Access key of the NFS-e generated from a DPS, or null when the DPS was not processed.</summary>
    Task<string?> FindAccessKeyAsync(NfseEnvironment environment, X509Certificate2 certificate, string dpsId, CancellationToken cancellationToken = default);

    Task<string> GetNfseXmlAsync(NfseEnvironment environment, X509Certificate2 certificate, string accessKey, CancellationToken cancellationToken = default);
}

public class HttpNationalNfseApi : INationalNfseApi
{
    public static string BaseUrl(NfseEnvironment environment) => environment == NfseEnvironment.Production
        ? "https://sefin.nfse.gov.br/SefinNacional"
        : "https://sefin.producaorestrita.nfse.gov.br/SefinNacional";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<NationalNfseResult> SendDpsAsync(NfseEnvironment environment, X509Certificate2 certificate, string signedDpsXml, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient(environment, certificate);
        using var response = await http.PostAsJsonAsync("nfse", new { dpsXmlGZipB64 = GZipBase64Encode(signedDpsXml) }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResult(response.StatusCode, body);
    }

    public async Task<string?> FindAccessKeyAsync(NfseEnvironment environment, X509Certificate2 certificate, string dpsId, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient(environment, certificate);
        using var response = await http.GetAsync($"dps/{dpsId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw UnexpectedResponse(response.StatusCode, body);

        using var json = JsonDocument.Parse(body);
        return GetString(json.RootElement, "chaveAcesso");
    }

    public async Task<string> GetNfseXmlAsync(NfseEnvironment environment, X509Certificate2 certificate, string accessKey, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient(environment, certificate);
        using var response = await http.GetAsync($"nfse/{accessKey}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw UnexpectedResponse(response.StatusCode, body);

        using var json = JsonDocument.Parse(body);
        return GZipBase64Decode(GetString(json.RootElement, "nfseXmlGZipB64")
            ?? throw UnexpectedResponse(response.StatusCode, body));
    }

    private static HttpClient CreateClient(NfseEnvironment environment, X509Certificate2 certificate)
    {
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };
        handler.ClientCertificates.Add(certificate);
        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(BaseUrl(environment) + "/"),
            Timeout = Timeout
        };
    }

    /// <summary>Reads the answer to POST /nfse. Success carries the key and the NFS-e; a rejection carries "erros".</summary>
    public static NationalNfseResult ParseResult(HttpStatusCode status, string body)
    {
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw UnexpectedResponse(status, body);
        }

        using (json)
        {
            var root = json.RootElement;
            var alerts = ReadMessages(root, "alertas");
            var accessKey = GetString(root, "chaveAcesso");
            var xml = GetString(root, "nfseXmlGZipB64");
            if ((int)status is >= 200 and < 300 && accessKey is not null && xml is not null)
                return new NationalNfseResult(accessKey, GZipBase64Decode(xml), [], alerts);

            var errors = ReadMessages(root, "erros").Concat(ReadMessages(root, "erro")).ToList();
            if (errors.Count == 0)
                throw UnexpectedResponse(status, body);
            return new NationalNfseResult(null, null, errors, alerts);
        }
    }

    // The API is not consistent about casing and about "erro" being an object or a list.
    private static List<NationalNfseMessage> ReadMessages(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !TryGetProperty(root, name, out var value))
            return [];

        var items = value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().ToList(),
            JsonValueKind.Object => [value],
            _ => []
        };

        return items
            .Select(x => new NationalNfseMessage(
                GetString(x, "codigo") ?? "?",
                GetString(x, "descricao") ?? "(no description)",
                GetString(x, "complemento")))
            .ToList();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static HttpRequestException UnexpectedResponse(HttpStatusCode status, string body)
    {
        var snippet = body.Length > 300 ? body[..300] + "..." : body;
        return new HttpRequestException($"Unexpected answer from the national NFS-e API (HTTP {(int)status}): {snippet}", null, status);
    }

    public static string GZipBase64Encode(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    public static string GZipBase64Decode(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
