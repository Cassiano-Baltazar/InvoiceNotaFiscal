using InvoiceApp.Web.Models;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace InvoiceApp.Web.Services;

public interface IPdfExportService
{
    Task<byte[]> GeneratePdfAsync(InvoiceReportModel model, CancellationToken cancellationToken = default);
    Task<string> SavePdfAsync(InvoiceReportModel model, string outputPath, CancellationToken cancellationToken = default);
    Task<byte[]> GeneratePdfFromHtmlAsync(string html, CancellationToken cancellationToken = default);
}

public class PdfExportService(ILogger<PdfExportService> logger) : IPdfExportService
{
    private static readonly string[] EdgePaths =
    [
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    ];

    public Task<byte[]> GeneratePdfAsync(InvoiceReportModel model, CancellationToken cancellationToken = default) =>
        GeneratePdfFromHtmlAsync(InvoiceHtmlBuilder.Build(model), cancellationToken);

    public async Task<byte[]> GeneratePdfFromHtmlAsync(string html, CancellationToken cancellationToken = default)
    {
        await using var browser = await LaunchBrowserAsync();
        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(html);
        return await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            MarginOptions = new MarginOptions
            {
                Top = "10mm",
                Bottom = "10mm",
                Left = "10mm",
                Right = "10mm"
            }
        });
    }

    public async Task<string> SavePdfAsync(InvoiceReportModel model, string outputPath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var pdfBytes = await GeneratePdfAsync(model, cancellationToken);
        await File.WriteAllBytesAsync(outputPath, pdfBytes, cancellationToken);
        logger.LogInformation("PDF saved to {Path}", outputPath);
        return outputPath;
    }

    private static async Task<IBrowser> LaunchBrowserAsync()
    {
        var executablePath = EdgePaths.FirstOrDefault(File.Exists);
        if (executablePath is not null)
        {
            return await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = executablePath,
                Args = ["--no-sandbox", "--disable-gpu"]
            });
        }

        var fetcher = new BrowserFetcher();
        await fetcher.DownloadAsync();
        return await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-gpu"]
        });
    }
}
