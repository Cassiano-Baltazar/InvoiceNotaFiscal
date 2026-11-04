namespace InvoiceApp.Tests.Infrastructure;

/// <summary>An NFS-e in the national layout, shaped like one returned by the Sefin Nacional, with sample data.</summary>
public static class NationalNfseSamples
{
    public const string AccessKey = "43093082211222333000144000000000000126112727063800";

    public static string Issued(string environment = "1") => $"""
        <?xml version="1.0" encoding="utf-8"?><NFSe versao="1.01" xmlns="http://www.sped.fazenda.gov.br/nfse"><infNFSe Id="NFS{AccessKey}">
        <xLocEmi>Guaíba</xLocEmi><xLocPrestacao>Canadá</xLocPrestacao><nNFSe>1</nNFSe>
        <xTribNac>Análise e desenvolvimento de sistemas.</xTribNac><xNBS>Serviços de desenvolvimento de aplicativos personalizados</xNBS>
        <cStat>100</cStat><dhProc>2026-11-04T10:27:31-03:00</dhProc>
        <emit><CNPJ>11222333000144</CNPJ><xNome>TEST COMPANY LTDA</xNome>
        <enderNac><xLgr>RUA EXEMPLO</xLgr><nro>100</nro><xBairro>CENTRO</xBairro><cMun>4309308</cMun><UF>RS</UF><CEP>92700000</CEP></enderNac>
        <email>billing@example.com</email></emit>
        <valores><vLiq>15487.82</vLiq></valores>
        <DPS versao="1.01"><infDPS Id="DPS430930821122233300014400001000000000000008">
        <tpAmb>{environment}</tpAmb><dhEmi>2026-11-04T10:26:29-03:00</dhEmi><serie>1</serie><nDPS>8</nDPS><dCompet>2026-10-31</dCompet>
        <prest><CNPJ>11222333000144</CNPJ><regTrib><opSimpNac>3</opSimpNac><regApTribSN>1</regApTribSN><regEspTrib>0</regEspTrib></regTrib></prest>
        <toma><cNaoNIF>2</cNaoNIF><xNome>SAMPLE CLIENT CANADA INC.</xNome>
        <end><endExt><cPais>CA</cPais><cEndPost>H2X 1Y4</cEndPost><xCidade>Montreal</xCidade><xEstProvReg>QC</xEstProvReg></endExt><xLgr>Example Street</xLgr><nro>10</nro></end></toma>
        <serv><locPrest><cPaisPrestacao>CA</cPaisPrestacao></locPrest>
        <cServ><cTribNac>010101</cTribNac><xDescServ>Software development services - October 2026</xDescServ><cNBS>115022000</cNBS></cServ>
        <comExt><tpMoeda>165</tpMoeda><vServMoeda>4000.00</vServMoeda></comExt>
        <infoCompl><xInfComp>Invoice INV-2026-11-001 - CAD 4,000.00</xInfComp></infoCompl></serv>
        <valores><vServPrest><vServ>15487.82</vServ></vServPrest><trib><tribMun><tribISSQN>3</tribISSQN><tpRetISSQN>1</tpRetISSQN></tribMun>
        <totTrib><pTotTribSN>3.05</pTotTribSN></totTrib></trib></valores>
        </infDPS></DPS></infNFSe></NFSe>
        """;
}
