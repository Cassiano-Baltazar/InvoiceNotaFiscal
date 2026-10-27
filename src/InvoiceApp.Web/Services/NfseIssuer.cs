using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Services;

public interface INfseIssuer
{
    /// <summary>Issues the draft through the national API (Emissor Nacional) in the environment chosen in Settings.</summary>
    Task<NfseActionResult> IssueAsync(int nfseId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Issues a draft: checks the data, reserves the DPS number, builds, validates and signs the DPS and sends it.
/// The reserved number is kept on the draft, so a retry after a failure sends the same DPS: if the first attempt
/// was processed but the answer was lost, the issued note is fetched instead of issuing a second one.
/// In the restricted production (test) environment the note has no fiscal value: the draft stays a draft.
/// </summary>
public class NfseIssuer(
    AppDbContext db,
    ICertificateService certificates,
    INationalNfseApi api,
    TimeProvider time,
    ILogger<NfseIssuer> logger) : INfseIssuer
{
    public async Task<NfseActionResult> IssueAsync(int nfseId, CancellationToken cancellationToken = default)
    {
        var nfse = await db.Nfses.Include(x => x.Invoice).FirstOrDefaultAsync(x => x.Id == nfseId, cancellationToken);
        if (nfse is null)
            return NfseActionResult.Fail("NFS-e not found.");
        if (nfse.Status != NfseStatus.Draft)
            return NfseActionResult.Fail("Only drafts can be issued.", nfseId);
        if (nfse.Invoice.Status == InvoiceStatus.Cancelled)
            return NfseActionResult.Fail("The invoice is cancelled.", nfseId);

        var company = await db.CompanySettings.FirstAsync(cancellationToken);
        var problems = DpsXml.Check(nfse, company);
        if (problems.Count > 0)
            return NfseActionResult.Fail("The NFS-e cannot be issued yet: " + string.Join(" ", problems), nfseId);

        var issued = await db.Nfses.AnyAsync(x => x.InvoiceId == nfse.InvoiceId && x.Status == NfseStatus.Issued, cancellationToken);
        if (issued && company.NfseEnvironment == NfseEnvironment.Production)
            return NfseActionResult.Fail("The invoice already has an issued NFS-e.", nfseId);

        System.Security.Cryptography.X509Certificates.X509Certificate2? certificate;
        try
        {
            certificate = await certificates.LoadAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return NfseActionResult.Fail(ex.Message, nfseId);
        }

        if (certificate is null)
            return NfseActionResult.Fail("Upload the digital certificate in Settings first.", nfseId);

        using (certificate)
        {
            var companyCnpj = DpsXml.Digits(company.Cnpj);
            if (!certificate.Subject.Contains(companyCnpj, StringComparison.Ordinal))
                return NfseActionResult.Fail($"The certificate ({certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)}) is not from the company CNPJ in Settings.", nfseId);
            if (certificate.NotAfter < time.GetLocalNow().DateTime)
                return NfseActionResult.Fail("The digital certificate has expired.", nfseId);

            var environment = company.NfseEnvironment;
            try
            {
                // A DPS sent before with no answer: the note may exist already.
                if (nfse.DpsId is not null && nfse.DpsEnvironment == environment)
                {
                    var existingKey = await api.FindAccessKeyAsync(environment, certificate, nfse.DpsId, cancellationToken);
                    if (existingKey is not null)
                    {
                        var existingXml = await api.GetNfseXmlAsync(environment, certificate, existingKey, cancellationToken);
                        return await CompleteAsync(nfse, environment, existingKey, existingXml, [], cancellationToken);
                    }
                }

                if (nfse.DpsId is null || nfse.DpsEnvironment != environment)
                    await ReserveNumberAsync(nfse, company, environment, cancellationToken);

                var document = DpsXml.Build(new DpsData(
                    nfse,
                    await db.Clients.FindAsync([nfse.Invoice.ClientId], cancellationToken),
                    company,
                    environment,
                    nfse.DpsSeries!,
                    nfse.DpsNumber!.Value,
                    time.GetUtcNow().AddMinutes(-1)));

                var schemaErrors = DpsXml.Validate(document);
                if (schemaErrors.Count > 0)
                    return await FailAsync(nfse, "The DPS does not match the official layout: " + string.Join(" ", schemaErrors), cancellationToken);

                var signed = DpsXml.Sign(document, certificate);
                var result = await api.SendDpsAsync(environment, certificate, signed, cancellationToken);
                if (!result.Issued)
                    return await FailAsync(nfse, "Rejected by the national NFS-e system: " + string.Join("; ", result.Errors), cancellationToken);

                return await CompleteAsync(nfse, environment, result.AccessKey!, result.NfseXml!, result.Alerts, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Security.Authentication.AuthenticationException)
            {
                logger.LogWarning(ex, "NFS-e {Id}: could not reach the national API", nfse.Id);
                return await FailAsync(nfse,
                    $"Could not reach the national NFS-e system ({ex.Message}). Try again: the app first checks whether the note was issued, so it is not issued twice.",
                    cancellationToken);
            }
        }
    }

    private async Task ReserveNumberAsync(Nfse nfse, CompanySettings company, NfseEnvironment environment, CancellationToken cancellationToken)
    {
        nfse.DpsSeries = company.DpsSeries;
        nfse.DpsNumber = company.NextDpsNumber;
        nfse.DpsId = DpsXml.BuildId(company.MunicipalityCode, DpsXml.Digits(company.Cnpj), company.DpsSeries, company.NextDpsNumber);
        nfse.DpsEnvironment = environment;
        company.NextDpsNumber++;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<NfseActionResult> CompleteAsync(
        Nfse nfse, NfseEnvironment environment, string accessKey, string nfseXml,
        IReadOnlyList<NationalNfseMessage> alerts, CancellationToken cancellationToken)
    {
        var (number, processedAt) = DpsXml.ReadIssued(nfseXml);
        var warnings = alerts.Select(x => $"Alert {x}").ToList();

        if (environment == NfseEnvironment.RestrictedProduction)
        {
            // Test note: keep the draft, and free it so the next attempt uses a new DPS number.
            nfse.DpsSeries = null;
            nfse.DpsNumber = null;
            nfse.DpsId = null;
            nfse.DpsEnvironment = null;
            nfse.IssueError = null;
            nfse.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return NfseActionResult.Ok(
                $"Test NFS-e nº {number} accepted in the restricted production environment (no fiscal value). Access key: {accessKey}. " +
                "Switch Settings to Production to issue it for real.", nfse.Id, warnings);
        }

        nfse.Status = NfseStatus.Issued;
        nfse.Source = NfseSource.NationalApi;
        nfse.Number = number;
        nfse.Series = null;
        nfse.IssuedAt = processedAt ?? time.GetLocalNow().DateTime;
        nfse.NationalAccessKey = accessKey;
        nfse.XmlContent = nfseXml;
        nfse.FileName = $"NFSe_{accessKey}.xml";
        nfse.MunicipalStatusDescription = "Emitida";
        nfse.ProviderCnpj = DpsXml.Digits((await db.CompanySettings.FirstAsync(cancellationToken)).Cnpj);
        nfse.IssueError = null;
        nfse.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NfseActionResult.Ok($"NFS-e nº {number} issued. Access key: {accessKey}.", nfse.Id, warnings);
    }

    private async Task<NfseActionResult> FailAsync(Nfse nfse, string message, CancellationToken cancellationToken)
    {
        nfse.IssueError = message;
        nfse.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NfseActionResult.Fail(message, nfse.Id);
    }
}
