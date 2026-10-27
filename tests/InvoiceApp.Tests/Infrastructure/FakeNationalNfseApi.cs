using System.Security.Cryptography.X509Certificates;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Infrastructure;

/// <summary>Stands in for the national NFS-e API: records what was sent and answers with what the test set.</summary>
public sealed class FakeNationalNfseApi : INationalNfseApi
{
    public const string AccessKey = "43093082112223330001440000000000000726110000000017";

    public List<(NfseEnvironment Environment, string Xml)> Sent { get; } = [];
    public List<string> Lookups { get; } = [];

    /// <summary>Answer to the next DPS. Null means issued with <see cref="IssuedXml"/>.</summary>
    public NationalNfseResult? NextResult { get; set; }
    public Exception? NextException { get; set; }
    public string? KeyForLookup { get; set; }

    public static string IssuedXml(string number = "7") =>
        $"""<NFSe xmlns="http://www.sped.fazenda.gov.br/nfse" versao="1.01"><infNFSe Id="NFS{AccessKey}"><nNFSe>{number}</nNFSe><dhProc>2026-11-03T10:15:00-03:00</dhProc></infNFSe></NFSe>""";

    public static NationalNfseResult Rejection(string code = "E0001", string description = "Rejected") =>
        new(null, null, [new NationalNfseMessage(code, description, null)], []);

    public Task<NationalNfseResult> SendDpsAsync(NfseEnvironment environment, X509Certificate2 certificate, string signedDpsXml, CancellationToken cancellationToken = default)
    {
        Sent.Add((environment, signedDpsXml));
        if (NextException is { } ex)
        {
            NextException = null;
            throw ex;
        }

        var result = NextResult ?? new NationalNfseResult(AccessKey, IssuedXml(), [], []);
        NextResult = null;
        return Task.FromResult(result);
    }

    public Task<string?> FindAccessKeyAsync(NfseEnvironment environment, X509Certificate2 certificate, string dpsId, CancellationToken cancellationToken = default)
    {
        Lookups.Add(dpsId);
        return Task.FromResult(KeyForLookup);
    }

    public Task<string> GetNfseXmlAsync(NfseEnvironment environment, X509Certificate2 certificate, string accessKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(IssuedXml("8"));
}
