using InvoiceApp.Web.Data;
using InvoiceApp.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceApp.Tests.Infrastructure;

public abstract class ServiceTestBase : IDisposable
{
    protected TestDatabase Database { get; } = new();
    protected FakePdfExportService Pdf { get; } = new();
    private readonly List<AppDbContext> contexts = [];

    protected AppDbContext NewContext()
    {
        var context = Database.CreateContext();
        contexts.Add(context);
        return context;
    }

    protected InvoiceService CreateInvoiceService()
    {
        var context = NewContext();
        return new InvoiceService(context, Pdf, new NfseService(context), NullLogger<InvoiceService>.Instance);
    }

    protected NfseService CreateNfseService() => new(NewContext());

    public void Dispose()
    {
        foreach (var context in contexts)
            context.Dispose();
        Database.Dispose();
        GC.SuppressFinalize(this);
    }
}
