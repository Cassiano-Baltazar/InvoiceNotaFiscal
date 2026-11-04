using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace InvoiceApp.Web.Services;

/// <summary>
/// Printable summary (DANFSe) of a note issued through the national system, built from its XML.
/// The official DANFSe API is suspended, so the app renders its own; the official one can still be
/// downloaded from the Emissor Nacional website.
/// </summary>
public static class DanfseHtmlBuilder
{
    private static readonly XNamespace Ns = DpsXml.Namespace;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public const string ConsultationUrl = "https://www.nfse.gov.br/ConsultaPublica";

    /// <summary>True for a note in the national layout; notes from the city hall portal use another one.</summary>
    public static bool IsNationalNfse(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants(Ns + "infNFSe").Any();
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    public static string Build(string nfseXml)
    {
        var doc = XDocument.Parse(nfseXml);
        var inf = doc.Descendants(Ns + "infNFSe").First();
        var dps = inf.Descendants(Ns + "infDPS").FirstOrDefault() ?? new XElement(Ns + "infDPS");
        var emit = inf.Element(Ns + "emit");
        var toma = dps.Element(Ns + "toma");
        var serv = dps.Element(Ns + "serv");
        var cServ = serv?.Element(Ns + "cServ");
        var comExt = serv?.Element(Ns + "comExt");
        var valores = dps.Element(Ns + "valores");
        var accessKey = ((string?)inf.Attribute("Id") ?? string.Empty).Replace("NFS", string.Empty);
        var environment = V(dps, "tpAmb") == "1" ? null : "SEM VALOR FISCAL - PRODUÇÃO RESTRITA";

        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <title>DANFSe</title>
            <style>
              body { font-family: Arial, Helvetica, sans-serif; color: #111; font-size: 11px; margin: 0; }
              h1 { font-size: 16px; margin: 0; }
              h2 { font-size: 11px; margin: 0 0 4px; text-transform: uppercase; background: #eee; padding: 3px 6px; }
              section { border: 1px solid #999; margin-bottom: 6px; }
              .body { padding: 4px 6px; }
              table { width: 100%; border-collapse: collapse; }
              td { vertical-align: top; padding: 2px 6px 2px 0; }
              .label { color: #555; font-size: 9px; text-transform: uppercase; display: block; }
              .warning { color: #b00; font-weight: 700; font-size: 14px; text-align: center; margin: 4px 0; }
              .key { font-family: monospace; font-size: 12px; word-break: break-all; }
              .right { text-align: right; }
            </style>
            </head>
            <body>
            """);

        sb.Append("<section><div class=\"body\"><table><tr>");
        sb.Append("<td><h1>DANFSe</h1>Documento Auxiliar da Nota Fiscal de Serviço Eletrônica</td>");
        sb.Append($"<td class=\"right\">{Field("Número da NFS-e", V(inf, "nNFSe"))}{Field("Data e hora da emissão", DateTime(V(inf, "dhProc")))}</td>");
        sb.Append("</tr></table>");
        if (environment is not null)
            sb.Append($"<p class=\"warning\">{E(environment)}</p>");
        sb.Append($"{Field("Chave de acesso", $"<span class=\"key\">{E(accessKey)}</span>", raw: true)}");
        sb.Append("<table><tr>");
        sb.Append($"<td>{Field("Competência", Date(V(dps, "dCompet")))}</td>");
        sb.Append($"<td>{Field("Série / Número da DPS", $"{V(dps, "serie")} / {V(dps, "nDPS")}")}</td>");
        sb.Append($"<td>{Field("Emissão da DPS", DateTime(V(dps, "dhEmi")))}</td>");
        sb.Append($"<td>{Field("Local de emissão", V(inf, "xLocEmi"))}</td>");
        sb.Append($"<td>{Field("Local da prestação", V(inf, "xLocPrestacao"))}</td>");
        sb.Append("</tr></table>");
        sb.Append($"{Field("Consulta da autenticidade", $"{ConsultationUrl} (informe a chave de acesso)")}");
        sb.Append("</div></section>");

        sb.Append("<section><h2>Prestador / Emitente</h2><div class=\"body\"><table><tr>");
        sb.Append($"<td>{Field("Nome / Razão social", V(emit, "xNome"))}</td>");
        sb.Append($"<td>{Field("CNPJ", Cnpj(V(emit, "CNPJ")))}</td>");
        sb.Append("</tr><tr>");
        var address = emit?.Element(Ns + "enderNac");
        sb.Append($"<td>{Field("Endereço", Join(V(address, "xLgr"), V(address, "nro"), V(address, "xBairro")))}</td>");
        sb.Append($"<td>{Field("Município / UF / CEP", Join(V(inf, "xLocEmi"), V(address, "UF"), V(address, "CEP")))}</td>");
        sb.Append("</tr><tr>");
        sb.Append($"<td>{Field("E-mail", V(emit, "email"))}</td>");
        sb.Append($"<td>{Field("Telefone", V(emit, "fone"))}</td>");
        sb.Append("</tr><tr>");
        sb.Append($"<td colspan=\"2\">{Field("Regime de tributação", SimplesLabel(V(dps.Element(Ns + "prest")?.Element(Ns + "regTrib"), "opSimpNac")))}</td>");
        sb.Append("</tr></table></div></section>");

        sb.Append("<section><h2>Tomador do serviço</h2><div class=\"body\"><table><tr>");
        sb.Append($"<td>{Field("Nome / Razão social", V(toma, "xNome"))}</td>");
        sb.Append($"<td>{Field("NIF / Documento", TakerDocument(toma))}</td>");
        sb.Append("</tr><tr>");
        var end = toma?.Element(Ns + "end");
        var endExt = end?.Element(Ns + "endExt");
        sb.Append($"<td>{Field("Endereço", Join(V(end, "xLgr"), V(end, "nro"), V(end, "xCpl")))}</td>");
        sb.Append($"<td>{Field("Cidade / Região / País / Código postal", Join(V(endExt, "xCidade"), V(endExt, "xEstProvReg"), V(endExt, "cPais"), V(endExt, "cEndPost")))}</td>");
        sb.Append("</tr></table></div></section>");

        sb.Append("<section><h2>Serviço prestado</h2><div class=\"body\">");
        sb.Append($"{Field("Código de tributação nacional", Join(V(cServ, "cTribNac"), V(inf, "xTribNac")))}");
        sb.Append($"{Field("Código NBS", Join(V(cServ, "cNBS"), V(inf, "xNBS")))}");
        sb.Append($"{Field("Descrição do serviço", V(cServ, "xDescServ"))}");
        if (comExt is not null)
        {
            sb.Append("<table><tr>");
            sb.Append($"<td>{Field("Moeda (código BACEN)", V(comExt, "tpMoeda"))}</td>");
            sb.Append($"<td>{Field("Valor na moeda estrangeira", Money(V(comExt, "vServMoeda")))}</td>");
            sb.Append("</tr></table>");
        }
        sb.Append("</div></section>");

        var tribMun = valores?.Element(Ns + "trib")?.Element(Ns + "tribMun");
        sb.Append("<section><h2>Valores e tributação</h2><div class=\"body\"><table><tr>");
        sb.Append($"<td>{Field("Valor do serviço", "R$ " + Money(V(valores?.Element(Ns + "vServPrest"), "vServ")))}</td>");
        sb.Append($"<td>{Field("Valor líquido", "R$ " + Money(V(inf.Element(Ns + "valores"), "vLiq")))}</td>");
        sb.Append($"<td>{Field("Tributação do ISSQN", IssLabel(V(tribMun, "tribISSQN")))}</td>");
        sb.Append($"<td>{Field("Retenção do ISSQN", V(tribMun, "tpRetISSQN") == "1" ? "Não retido" : "Retido")}</td>");
        var simplesRate = V(valores?.Element(Ns + "trib")?.Element(Ns + "totTrib"), "pTotTribSN");
        if (!string.IsNullOrEmpty(simplesRate))
            sb.Append($"<td>{Field("Tributos aproximados (Simples Nacional)", Money(simplesRate) + "%")}</td>");
        sb.Append("</tr></table></div></section>");

        var notes = V(serv?.Element(Ns + "infoCompl"), "xInfComp");
        if (!string.IsNullOrEmpty(notes))
            sb.Append($"<section><h2>Informações complementares</h2><div class=\"body\">{E(notes)}</div></section>");

        sb.Append("<p>Documento gerado pelo InvoiceApp a partir do XML da NFS-e. A versão oficial pode ser consultada pela chave de acesso no portal nfse.gov.br.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string V(XElement? parent, string name) => parent?.Element(Ns + name)?.Value.Trim() ?? string.Empty;

    private static string Field(string label, string value, bool raw = false) =>
        $"<div><span class=\"label\">{E(label)}</span>{(raw ? value : E(string.IsNullOrEmpty(value) ? "—" : value))}</div>";

    private static string Join(params string[] values) => string.Join(" - ", values.Where(x => !string.IsNullOrEmpty(x)));

    private static string Money(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount.ToString("N2", PtBr) : value;

    private static string Date(string value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date) ? date.ToString("dd/MM/yyyy") : value;

    private static string DateTime(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToString("dd/MM/yyyy HH:mm:ss") : value;

    private static string Cnpj(string value) =>
        value.Length == 14 ? $"{value[..2]}.{value[2..5]}.{value[5..8]}/{value[8..12]}-{value[12..]}" : value;

    private static string TakerDocument(XElement? toma)
    {
        var nif = V(toma, "NIF");
        if (!string.IsNullOrEmpty(nif))
            return "NIF " + nif;
        var cnpj = V(toma, "CNPJ");
        if (!string.IsNullOrEmpty(cnpj))
            return "CNPJ " + Cnpj(cnpj);
        return V(toma, "cNaoNIF") switch
        {
            "1" => "NIF dispensado",
            "2" => "NIF não exigido",
            _ => "Não informado"
        };
    }

    private static string SimplesLabel(string value) => value switch
    {
        "1" => "Não optante do Simples Nacional",
        "2" => "Simples Nacional - MEI",
        "3" => "Simples Nacional - ME/EPP",
        _ => value
    };

    private static string IssLabel(string value) => value switch
    {
        "1" => "Operação tributável",
        "2" => "Imunidade",
        "3" => "Exportação de serviço",
        "4" => "Não incidência",
        _ => value
    };

    // Only the markup characters: the page is UTF-8, so accented text stays readable in the HTML.
    private static string E(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
