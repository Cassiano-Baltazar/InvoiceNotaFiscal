using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using InvoiceApp.Web.Data.Entities;

namespace InvoiceApp.Web.Services;

/// <summary>Data read from an NFS-e XML exported by the city hall portal (IPM / Atende.net layout).</summary>
public class NfseXmlData
{
    public string? Number { get; init; }
    public string? Series { get; init; }
    public DateTime? IssuedAt { get; init; }
    public DateOnly? ServiceDate { get; init; }
    public int? MunicipalStatusCode { get; init; }
    public string? MunicipalStatusDescription { get; init; }
    public NfseStatus Status { get; init; }
    public string? VerificationCode { get; init; }
    public string? NationalAccessKey { get; init; }
    public string? Link { get; init; }
    public string? ProviderCnpj { get; init; }
    public string TakerName { get; init; } = string.Empty;
    public string? TakerCountry { get; init; }
    public string? TakerCountryCode { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public string? ActivityCode { get; init; }
    public string? ServiceListItem { get; init; }
    public string? NbsCode { get; init; }
    public string? TaxSituation { get; init; }
    public decimal IssRate { get; init; }
    public decimal AmountBrl { get; init; }
    public decimal Discount { get; init; }
    public decimal IrAmount { get; init; }
    public decimal InssAmount { get; init; }
    public decimal CsllAmount { get; init; }
    public decimal PisAmount { get; init; }
    public decimal CofinsAmount { get; init; }
    public decimal IssWithheldAmount { get; init; }
    public string XmlContent { get; init; } = string.Empty;
}

public static class NfseXmlParser
{
    /// <summary>Reads the XML bytes, honoring the encoding declared in the file (the portal uses ISO-8859-1).</summary>
    public static NfseXmlData Parse(byte[] content)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"Invalid XML: {ex.Message}", ex);
        }

        return Parse(document);
    }

    public static NfseXmlData Parse(string xml) => Parse(Encoding.UTF8.GetBytes(StripDeclaration(xml)));

    private static NfseXmlData Parse(XDocument document)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "nfse")
            throw new FormatException("This file is not an NFS-e XML (missing <nfse> root).");

        var nf = root.Element("nf") ?? throw new FormatException("This file is not an NFS-e XML (missing <nf>).");
        var provider = root.Element("prestador");
        var taker = root.Element("tomador");
        var items = root.Element("itens")?.Elements("lista").ToList() ?? [];

        var statusCode = ParseInt(Text(nf, "situacao_codigo_nfse"));
        var issueDate = ParseDate(Text(nf, "data_nfse"));
        var issueTime = Text(nf, "hora_nfse");
        DateTime? issuedAt = null;
        if (issueDate is DateOnly date && date.Year > 1900)
        {
            var time = TimeOnly.TryParseExact(issueTime, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : TimeOnly.MinValue;
            issuedAt = date.ToDateTime(time);
        }

        var firstItem = items.FirstOrDefault();
        var number = Text(nf, "numero_nfse");

        return new NfseXmlData
        {
            Number = number,
            Series = Text(nf, "serie_nfse"),
            IssuedAt = issuedAt,
            ServiceDate = ParseDate(Text(nf, "data_fato") ?? Text(nf, "data_fato_gerador")),
            MunicipalStatusCode = statusCode,
            MunicipalStatusDescription = Text(nf, "situacao_descricao_nfse"),
            Status = MapStatus(statusCode, number),
            VerificationCode = Text(nf, "cod_verificador_autenticidade"),
            NationalAccessKey = Text(nf, "chave_acesso_nfse_nacional"),
            Link = Text(nf, "link_nfse"),
            ProviderCnpj = Text(provider, "cpfcnpj"),
            TakerName = Text(taker, "nome_razao_social") ?? Text(taker, "sobrenome_nome_fantasia") ?? string.Empty,
            TakerCountry = Text(taker, "pais"),
            TakerCountryCode = Text(taker, "siglaPais"),
            Description = string.Join(Environment.NewLine, items.Select(i => Text(i, "descritivo")).Where(d => d is not null)),
            Notes = CleanNotes(Text(nf, "observacao")),
            ActivityCode = Text(firstItem, "codigo_atividade"),
            ServiceListItem = Text(firstItem, "codigo_item_lista_servico"),
            NbsCode = Text(firstItem, "codigo_nbs"),
            TaxSituation = Text(firstItem, "situacao_tributaria"),
            IssRate = ParseDecimal(Text(firstItem, "aliquota_item_lista_servico")),
            AmountBrl = ParseDecimal(Text(nf, "valor_total")),
            Discount = ParseDecimal(Text(nf, "valor_desconto")),
            IrAmount = ParseDecimal(Text(nf, "valor_ir")),
            InssAmount = ParseDecimal(Text(nf, "valor_inss")),
            CsllAmount = ParseDecimal(Text(nf, "valor_contribuicao_social")),
            PisAmount = ParseDecimal(Text(nf, "valor_pis")),
            CofinsAmount = ParseDecimal(Text(nf, "valor_cofins")),
            IssWithheldAmount = items.Sum(i => ParseDecimal(Text(i, "valor_issrf"))),
            XmlContent = document.ToString(SaveOptions.None)
        };
    }

    /// <summary>Municipal status: 1 issued, 2 cancelled, 3 replaced (substituída).</summary>
    private static NfseStatus MapStatus(int? code, string? number) => code switch
    {
        1 => NfseStatus.Issued,
        2 => NfseStatus.Cancelled,
        3 => NfseStatus.Replaced,
        _ => string.IsNullOrWhiteSpace(number) ? NfseStatus.Draft : NfseStatus.Issued
    };

    private static string? Text(XElement? parent, string name)
    {
        var value = parent?.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // The portal writes "0" when there is no observation.
    private static string? CleanNotes(string? notes) => notes is null or "0" ? null : notes;

    /// <summary>Brazilian format: "15.207,14" or "2,0100".</summary>
    public static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        var normalized = value.Trim().Replace(".", string.Empty).Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string StripDeclaration(string xml)
    {
        var trimmed = xml.TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (!trimmed.StartsWith("<?xml", StringComparison.Ordinal))
            return trimmed;
        var end = trimmed.IndexOf("?>", StringComparison.Ordinal);
        return end < 0 ? trimmed : trimmed[(end + 2)..];
    }
}
