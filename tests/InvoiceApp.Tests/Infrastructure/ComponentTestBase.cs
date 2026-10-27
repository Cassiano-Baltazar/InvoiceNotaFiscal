using Bunit;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceApp.Tests.Infrastructure;

/// <summary>
/// bUnit base with the real services on top of an in-memory SQLite database.
/// Services are scoped like in the app, where one Blazor circuit shares one DbContext.
/// </summary>
public abstract class ComponentTestBase : BunitContext
{
    protected TestDatabase Database { get; } = new();
    protected FakePdfExportService Pdf { get; } = new();

    protected ComponentTestBase()
    {
        Services.AddDbContext<AppDbContext>(o => o.UseSqlite(Database.Connection));
        Services.AddScoped<IClientService, ClientService>();
        Services.AddScoped<ICompanySettingsService, CompanySettingsService>();
        Services.AddSingleton<ISecretProtector>(new AesSecretProtector(new byte[32]));
        Services.AddScoped<ICertificateService, CertificateService>();
        Services.AddScoped<INfseService, NfseService>();
        Services.AddScoped<IInvoiceService, InvoiceService>();
        Services.AddSingleton<IPdfExportService>(Pdf);
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Strict;
    }

    protected void ConfirmDialogsReturn(bool answer) =>
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(answer);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            Database.Dispose();
    }
}
