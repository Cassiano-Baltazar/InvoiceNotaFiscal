using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using InvoiceApp.Web.Data.Entities;

namespace InvoiceApp.Web.Services;

/// <summary>Everything the DPS needs, gathered from the draft, its invoice, the client and the settings.</summary>
public record DpsData(
    Nfse Nfse,
    Client? Client,
    CompanySettings Company,
    NfseEnvironment Environment,
    string Series,
    long Number,
    DateTimeOffset IssuedAt);

/// <summary>
/// Builds, validates and signs the DPS (Declaração de Prestação de Serviço) of the national NFS-e, layout 1.01,
/// for a Simples Nacional ME/EPP company exporting a service to a foreign taker.
/// </summary>
public static partial class DpsXml
{
    public const string Namespace = "http://www.sped.fazenda.gov.br/nfse";
    public const string Version = "1.01";
    public const string AppVersion = "InvoiceApp 1.0";
    private static readonly XNamespace Ns = Namespace;
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    /// <summary>ISO 4217 code to the BACEN code required in tpMoeda.</summary>
    public static readonly IReadOnlyDictionary<string, string> BacenCurrencyCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = "220",
        ["EUR"] = "978",
        ["GBP"] = "540",
        ["CAD"] = "165",
        ["AUD"] = "150",
        ["CHF"] = "425",
        ["JPY"] = "470",
        ["NZD"] = "245",
        ["BRL"] = "790"
    };

    /// <summary>"DPS" + municipality (7) + 2 (CNPJ) + CNPJ (14) + series (5) + number (15).</summary>
    public static string BuildId(string municipalityCode, string cnpj, string series, long number) =>
        $"DPS{municipalityCode}2{cnpj}{series.PadLeft(5, '0')}{number.ToString(CultureInfo.InvariantCulture).PadLeft(15, '0')}";

    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>Problems that would make the DPS invalid, in words the user can act on. Empty when it can be sent.</summary>
    public static List<string> Check(Nfse nfse, CompanySettings company)
    {
        var problems = new List<string>();
        if (Digits(company.Cnpj).Length != 14)
            problems.Add("Settings: the company CNPJ must have 14 digits.");
        if (!Regex.IsMatch(company.MunicipalityCode ?? "", @"^\d{7}$"))
            problems.Add("Settings: the municipality code (IBGE) must have 7 digits, e.g. 4309308 for Guaíba.");
        if (!Regex.IsMatch(company.DpsSeries ?? "", @"^(?:[0-9]{1,4}|[0-8][0-9]{4})$"))
            problems.Add("Settings: the DPS series must be a number from 0 to 89999.");
        if (company.SimplesNacionalTaxRate is <= 0 or >= 100)
            problems.Add("Settings: fill in the approximate Simples Nacional tax rate (%).");

        if (nfse.AmountBrl <= 0)
            problems.Add("Amount (BRL) must be greater than zero.");
        if (nfse.AmountForeign <= 0)
            problems.Add("Amount in the foreign currency must be greater than zero.");
        if (string.IsNullOrWhiteSpace(nfse.CurrencyCode) || !BacenCurrencyCodes.ContainsKey(nfse.CurrencyCode))
            problems.Add($"Currency '{nfse.CurrencyCode}' is not supported. Use one of: {string.Join(", ", BacenCurrencyCodes.Keys)}.");
        if (!IsCountryCode(nfse.TakerCountryCode))
            problems.Add("Taker country code (ISO, 2 letters) is required.");
        if (!IsCountryCode(nfse.ResultCountryCode))
            problems.Add("The country where the result is used (ISO, 2 letters) is required.");
        if (string.IsNullOrWhiteSpace(nfse.TakerTaxId) && nfse.TakerMissingTaxIdReason is null)
            problems.Add("Fill in the taker tax id (NIF) or the reason it has none.");
        if (!Regex.IsMatch(nfse.NationalTaxCode ?? "", @"^\d{6}$"))
            problems.Add("National tax code must have 6 digits, e.g. 010101.");
        if (!string.IsNullOrWhiteSpace(nfse.NbsCode) && !Regex.IsMatch(nfse.NbsCode, @"^\d{9}$"))
            problems.Add("NBS code must have 9 digits.");
        if (nfse.IrAmount + nfse.InssAmount + nfse.CsllAmount + nfse.PisAmount + nfse.CofinsAmount + nfse.IssWithheldAmount > 0)
            problems.Add("Withheld taxes are not supported when issuing an exported service. Set them to zero.");
        return problems;
    }

    public static XmlDocument Build(DpsData data)
    {
        var nfse = data.Nfse;
        var company = data.Company;
        var cnpj = Digits(company.Cnpj);
        var issuedAt = data.IssuedAt.ToOffset(BrazilOffset);
        var competence = nfse.ServiceDate is { } serviceDate && serviceDate <= DateOnly.FromDateTime(issuedAt.DateTime)
            ? serviceDate
            : DateOnly.FromDateTime(issuedAt.DateTime);

        var infDps = new XElement(Ns + "infDPS",
            new XAttribute("Id", BuildId(company.MunicipalityCode, cnpj, data.Series, data.Number)),
            new XElement(Ns + "tpAmb", (int)data.Environment),
            new XElement(Ns + "dhEmi", issuedAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "-03:00"),
            new XElement(Ns + "verAplic", AppVersion),
            new XElement(Ns + "serie", data.Series),
            new XElement(Ns + "nDPS", data.Number.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns + "dCompet", competence.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(Ns + "tpEmit", 1),
            new XElement(Ns + "cLocEmi", company.MunicipalityCode),
            // Name and address of the provider come from the CNPJ registration; the API rejects them when sent.
            new XElement(Ns + "prest",
                new XElement(Ns + "CNPJ", cnpj),
                new XElement(Ns + "regTrib",
                    new XElement(Ns + "opSimpNac", 3),
                    new XElement(Ns + "regApTribSN", 1),
                    new XElement(Ns + "regEspTrib", 0))),
            Taker(nfse, data.Client),
            new XElement(Ns + "serv",
                new XElement(Ns + "locPrest",
                    new XElement(Ns + "cLocPrestacao", company.MunicipalityCode)),
                new XElement(Ns + "cServ",
                    new XElement(Ns + "cTribNac", nfse.NationalTaxCode),
                    new XElement(Ns + "xDescServ", Cut(nfse.Description, 2000)),
                    string.IsNullOrWhiteSpace(nfse.NbsCode) ? null : new XElement(Ns + "cNBS", nfse.NbsCode)),
                new XElement(Ns + "comExt",
                    new XElement(Ns + "mdPrestacao", (int)nfse.ServiceMode),
                    new XElement(Ns + "vincPrest", nfse.RelatedToTaker ? 6 : 0),
                    new XElement(Ns + "tpMoeda", BacenCurrencyCodes[nfse.CurrencyCode!]),
                    new XElement(Ns + "vServMoeda", Money(nfse.AmountForeign)),
                    new XElement(Ns + "mecAFComexP", "01"),
                    new XElement(Ns + "mecAFComexT", "01"),
                    new XElement(Ns + "movTempBens", 1),
                    new XElement(Ns + "mdic", 0)),
                string.IsNullOrWhiteSpace(nfse.Notes)
                    ? null
                    : new XElement(Ns + "infoCompl", new XElement(Ns + "xInfComp", Cut(nfse.Notes, 2000)))),
            new XElement(Ns + "valores",
                new XElement(Ns + "vServPrest",
                    new XElement(Ns + "vServ", Money(nfse.AmountBrl))),
                new XElement(Ns + "trib",
                    // Export of services: no ISS (LC 116, art. 2, I), so no rate and no withholding.
                    new XElement(Ns + "tribMun",
                        new XElement(Ns + "tribISSQN", 3),
                        new XElement(Ns + "cPaisResult", nfse.ResultCountryCode!.ToUpperInvariant()),
                        new XElement(Ns + "tpRetISSQN", 1)),
                    new XElement(Ns + "totTrib",
                        new XElement(Ns + "pTotTribSN", company.SimplesNacionalTaxRate.ToString("0.00", CultureInfo.InvariantCulture))))));

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "DPS", new XAttribute("versao", Version), infDps));

        var xml = new XmlDocument { PreserveWhitespace = true };
        using var reader = document.CreateReader();
        xml.Load(reader);
        return xml;
    }

    private static XElement Taker(Nfse nfse, Client? client)
    {
        var taxId = nfse.TakerTaxId?.Trim();
        return new XElement(Ns + "toma",
            string.IsNullOrEmpty(taxId)
                ? new XElement(Ns + "cNaoNIF", (int)(nfse.TakerMissingTaxIdReason ?? MissingTaxIdReason.NotInformed))
                : new XElement(Ns + "NIF", Cut(taxId, 40)),
            new XElement(Ns + "xNome", Cut(nfse.TakerName, 300)),
            TakerAddress(nfse.TakerCountryCode!, client));
    }

    /// <summary>
    /// The client address is free text; the DPS wants street, number and district apart. The number is taken
    /// from the start or the end of the first line ("100 Example Street", "Example Street, 100"), the district is
    /// the second line (or the city). Without street, city and postal code the optional address is left out.
    /// </summary>
    private static XElement? TakerAddress(string countryCode, Client? client)
    {
        if (client is null || string.IsNullOrWhiteSpace(client.AddressLine1) || string.IsNullOrWhiteSpace(client.City)
            || string.IsNullOrWhiteSpace(client.PostalCode))
            return null;

        var line = client.AddressLine1.Trim();
        string street, number;
        if (LeadingNumber().Match(line) is { Success: true } leading)
            (number, street) = (leading.Groups[1].Value, leading.Groups[2].Value);
        else if (TrailingNumber().Match(line) is { Success: true } trailing)
            (street, number) = (trailing.Groups[1].Value, trailing.Groups[2].Value);
        else
            (street, number) = (line, "S/N");

        var district = string.IsNullOrWhiteSpace(client.AddressLine2) ? client.City.Trim() : client.AddressLine2.Trim();
        var complement = string.IsNullOrWhiteSpace(client.AddressLine2) ? null : client.AddressLine3?.Trim();

        return new XElement(Ns + "end",
            new XElement(Ns + "endExt",
                new XElement(Ns + "cPais", countryCode.ToUpperInvariant()),
                new XElement(Ns + "cEndPost", Cut(client.PostalCode.Trim(), 11)),
                new XElement(Ns + "xCidade", Cut(client.City.Trim(), 60)),
                new XElement(Ns + "xEstProvReg", Cut(string.IsNullOrWhiteSpace(client.Region) ? client.City.Trim() : client.Region.Trim(), 60))),
            new XElement(Ns + "xLgr", Cut(street, 255)),
            new XElement(Ns + "nro", Cut(number, 60)),
            string.IsNullOrWhiteSpace(complement) ? null : new XElement(Ns + "xCpl", Cut(complement, 156)),
            new XElement(Ns + "xBairro", Cut(district, 60)));
    }

    [GeneratedRegex(@"^(\d+[A-Za-z]?)\s+(.+)$")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"^(.+?),?\s+(\d+[A-Za-z]?)$")]
    private static partial Regex TrailingNumber();

    private static bool IsCountryCode(string? value) => value is { Length: 2 } && value.All(char.IsAsciiLetter);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    // ---------- Schema validation ----------

    private static readonly Lazy<XmlSchemaSet> Schemas = new(LoadSchemas);

    /// <summary>Validates against the official XSD (layout 1.01). Returns the errors found.</summary>
    public static List<string> Validate(XmlDocument document)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = Schemas.Value };
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
        using var reader = XmlReader.Create(new StringReader(document.OuterXml), settings);
        while (reader.Read())
        {
        }
        return errors;
    }

    private static XmlSchemaSet LoadSchemas()
    {
        var set = new XmlSchemaSet { XmlResolver = new EmbeddedSchemaResolver() };
        using var stream = OpenSchema("DPS_v1.01.xsd");
        using var reader = XmlReader.Create(stream, new XmlReaderSettings(), "embedded:///DPS_v1.01.xsd");
        set.Add(Namespace, reader);
        set.Compile();
        return set;
    }

    private static Stream OpenSchema(string fileName) =>
        typeof(DpsXml).Assembly.GetManifestResourceStream("Nfse." + fileName)
        ?? throw new InvalidOperationException($"Schema {fileName} is missing from the app.");

    private sealed class EmbeddedSchemaResolver : XmlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            return OpenSchema(Path.GetFileName(absoluteUri.AbsolutePath));
        }

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
            new($"embedded:///{Path.GetFileName(relativeUri ?? string.Empty)}");
    }

    // ---------- Signature ----------

    /// <summary>
    /// Signs infDPS (XMLDSig enveloped, RSA-SHA256, exclusive canonicalization) and appends the signature to DPS.
    /// Returns the signed XML to send, exactly as signed.
    /// </summary>
    public static string Sign(XmlDocument document, X509Certificate2 certificate)
    {
        var infDps = (XmlElement)(document.GetElementsByTagName("infDPS", Namespace)[0]
            ?? throw new InvalidOperationException("infDPS not found."));
        var rsa = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The certificate has no RSA private key.");

        var signedXml = new SignedXml(document) { SigningKey = rsa };
        signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference("#" + infDps.GetAttribute("Id")) { DigestMethod = SignedXml.XmlDsigSHA256Url };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificate));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        document.DocumentElement!.AppendChild(document.ImportNode(signedXml.GetXml(), true));

        var declaration = document.FirstChild is XmlDeclaration ? string.Empty : "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
        return declaration + document.OuterXml;
    }

    // ---------- Issued NFS-e ----------

    /// <summary>Number and processing time read from the NFS-e XML returned by the API.</summary>
    public static (string? Number, DateTime? ProcessedAt) ReadIssued(string nfseXml)
    {
        var document = XDocument.Parse(nfseXml);
        var info = document.Descendants(Ns + "infNFSe").FirstOrDefault();
        var number = info?.Element(Ns + "nNFSe")?.Value;
        DateTime? processedAt = DateTimeOffset.TryParse(info?.Element(Ns + "dhProc")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dh)
            ? dh.ToOffset(BrazilOffset).DateTime
            : null;
        return (number, processedAt);
    }
}
