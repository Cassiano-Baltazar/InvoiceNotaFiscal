using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InvoiceApp.Tests.Api;

/// <summary>Runs the real app in memory with a test database, a fake PDF service and no browser launch.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public TestDatabase Database { get; } = new();
    public FakePdfExportService Pdf { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("App:OpenBrowser", "false");
        builder.UseSetting("App:CertificateKeyPath", Path.Combine(Path.GetTempPath(), "invoiceapp-tests", Guid.NewGuid().ToString("N"), "certificate.key"));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(Database.Connection));
            services.RemoveAll<IPdfExportService>();
            services.AddSingleton<IPdfExportService>(Pdf);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            Database.Dispose();
    }
}
