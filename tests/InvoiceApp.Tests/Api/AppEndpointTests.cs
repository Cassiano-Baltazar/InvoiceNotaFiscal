using System.Net;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceApp.Tests.Api;

public class AppEndpointTests : IClassFixture<AppFactory>
{
    private readonly AppFactory factory;
    private readonly HttpClient client;

    public AppEndpointTests(AppFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
    }

    private int SeedInvoice()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clientId = db.Clients.OrderBy(x => x.Id).Select(x => x.Id).First();
        return factory.Database.SeedInvoice(clientId, number: $"INV-T-{Guid.NewGuid():N}"[..20]).Id;
    }

    [Fact]
    public async Task Startup_seeds_the_database()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("YOUR COMPANY", (await db.CompanySettings.SingleAsync()).TradeName);
        Assert.True(await db.Clients.CountAsync() >= 2);
    }

    [Fact]
    public async Task Pdf_endpoint_returns_pdf_file()
    {
        var id = SeedInvoice();

        var response = await client.GetAsync($"/api/invoices/{id}/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"invoice-{id}.pdf", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(FakePdfExportService.PdfBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Pdf_endpoint_returns_404_for_unknown_invoice()
    {
        var response = await client.GetAsync("/api/invoices/99999/pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/", "Dashboard")]
    [InlineData("/clients", "Sample Client")]
    [InlineData("/invoices", "Invoices")]
    [InlineData("/invoices/generate", "Generate Monthly Invoices")]
    [InlineData("/settings", "Company Settings")]
    public async Task Pages_render(string url, string expectedText)
    {
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invoice_pages_render_for_existing_invoice()
    {
        var id = SeedInvoice();

        var detail = await client.GetStringAsync($"/invoices/{id}");
        var edit = await client.GetStringAsync($"/invoices/{id}/edit");

        Assert.Contains("Mark as Sent", detail);
        Assert.Contains("Edit INV-T-", edit);
    }
}
