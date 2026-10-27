using InvoiceApp.Web.Models;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Infrastructure;

/// <summary>Replaces the Edge/Chrome based PDF export so tests do not need a browser.</summary>
public class FakePdfExportService : IPdfExportService
{
    public static readonly byte[] PdfBytes = "%PDF-1.4 fake"u8.ToArray();

    public bool ShouldFail { get; set; }
    public List<string> SavedPaths { get; } = [];
    public List<string> GeneratedInvoiceNumbers { get; } = [];

    public Task<byte[]> GeneratePdfAsync(InvoiceReportModel model, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
            throw new InvalidOperationException("Browser not available");

        GeneratedInvoiceNumbers.Add(model.Invoice.InvoiceNumber);
        return Task.FromResult(PdfBytes);
    }

    public async Task<string> SavePdfAsync(InvoiceReportModel model, string outputPath, CancellationToken cancellationToken = default)
    {
        var bytes = await GeneratePdfAsync(model, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken);
        SavedPaths.Add(outputPath);
        return outputPath;
    }
}
