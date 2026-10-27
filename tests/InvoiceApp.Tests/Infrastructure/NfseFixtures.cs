namespace InvoiceApp.Tests.Infrastructure;

/// <summary>
/// Real XML files from the city hall portal (IPM / Atende.net), with the company and client data replaced.
/// They keep the original ISO-8859-1 encoding and Brazilian number format.
/// </summary>
public static class NfseFixtures
{
    /// <summary>NFS-e nº 5, issued, SAMPLE CLIENT CANADA INC., R$ 15.207,14, "September 2026".</summary>
    public const string IssuedCanada = "issued-ca.xml";

    /// <summary>NFS-e nº 6, issued, SAMPLE CLIENT UK LTD, R$ 6.547,91, "September 2026".</summary>
    public const string IssuedUk = "issued-uk.xml";

    /// <summary>NFS-e nº 1, replaced (substituída), SAMPLE CLIENT UK LTD, R$ 6.858,05, "September 2026 - GBP 1,000.00".</summary>
    public const string ReplacedUk = "replaced-uk.xml";

    public const string CompanyCnpj = "11.222.333/0001-44";

    public static byte[] Read(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Nfse", name));

    public static byte[] Xml(string body, string encoding = "UTF-8") =>
        System.Text.Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"{encoding}\"?>{body}");
}
