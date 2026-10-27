using System.Diagnostics;
using InvoiceApp.Web.Components;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Services;
using Microsoft.EntityFrameworkCore;

const int DefaultPort = 5099;

var builder = WebApplication.CreateBuilder(args);

var port = builder.Configuration.GetValue("App:Port", DefaultPort);
var appUrl = $"http://localhost:{port}";

builder.WebHost.UseUrls(appUrl);

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "invoiceapp.db")}"));

builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<ICompanySettingsService, CompanySettingsService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddSingleton<IPdfExportService, PdfExportService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services, app.Environment.ContentRootPath);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();

app.MapGet("/api/invoices/{id:int}/pdf", async (int id, IInvoiceService invoiceService) =>
{
    try
    {
        var pdf = await invoiceService.ExportPdfAsync(id);
        return Results.File(pdf, "application/pdf", $"invoice-{id}.pdf");
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (builder.Configuration.GetValue("App:OpenBrowser", true))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo(appUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not open browser at {Url}", appUrl);
        }
    });
}

Console.WriteLine($"Invoice App running at {appUrl}");
Console.WriteLine("Press Ctrl+C to stop.");

app.Run();
public partial class Program;
