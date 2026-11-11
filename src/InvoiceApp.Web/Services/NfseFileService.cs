using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using Microsoft.EntityFrameworkCore;
using static InvoiceApp.Web.Services.Ui;

namespace InvoiceApp.Web.Services;

public interface INfseFileService
{
    /// <summary>Saves the XML and the DANFSe PDF of an issued note to {OutputBasePath}/{YYYY-MM}/NFSe/, the invoice's month folder.</summary>
    Task<NfseActionResult> SaveFilesAsync(int nfseId, CancellationToken cancellationToken = default);
}

public class NfseFileService(AppDbContext db, IPdfExportService pdf, ILogger<NfseFileService> logger) : INfseFileService
{
    public async Task<NfseActionResult> SaveFilesAsync(int nfseId, CancellationToken cancellationToken = default)
    {
        var nfse = await db.Nfses.AsNoTracking().Include(x => x.Invoice).FirstOrDefaultAsync(x => x.Id == nfseId, cancellationToken);
        if (nfse is null)
            return NfseActionResult.Fail(T("NFS-e not found."));
        if (nfse.Status == NfseStatus.Draft || string.IsNullOrWhiteSpace(nfse.XmlContent))
            return NfseActionResult.Fail(T("Only issued notes with their XML can be saved to the folder."), nfseId);

        var company = await db.CompanySettings.AsNoTracking().FirstAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(company.OutputBasePath))
            return NfseActionResult.Fail(T("Set the output folder in Settings first."), nfseId);

        var folder = Path.Combine(company.OutputBasePath, $"{nfse.Invoice.IssueYear}-{nfse.Invoice.IssueMonth:D2}", "NFSe");
        var baseName = Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(nfse.FileName) ? $"NFSe_{nfse.NationalAccessKey ?? nfse.Number ?? nfse.Id.ToString()}" : nfse.FileName);
        var xmlPath = Path.Combine(folder, baseName + ".xml");
        var pdfPath = Path.Combine(folder, baseName + ".pdf");

        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(xmlPath, nfse.XmlContent, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return NfseActionResult.Fail(T("Could not save the XML to {0}: {1}", folder, ex.Message), nfseId);
        }

        // Notes from the city hall portal use another layout; only the national one has a DANFSe here.
        if (!DanfseHtmlBuilder.IsNationalNfse(nfse.XmlContent))
            return NfseActionResult.Ok(T("XML saved to {0}.", xmlPath), nfseId);

        try
        {
            var bytes = await pdf.GeneratePdfFromHtmlAsync(DanfseHtmlBuilder.Build(nfse.XmlContent), cancellationToken);
            await File.WriteAllBytesAsync(pdfPath, bytes, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NFS-e {Id}: could not create the PDF", nfseId);
            return NfseActionResult.Ok(T("XML saved to {0}.", xmlPath), nfseId, [T("The PDF could not be created: {0}", ex.Message)]);
        }

        return NfseActionResult.Ok(T("XML and PDF saved to {0}.", folder), nfseId);
    }
}
