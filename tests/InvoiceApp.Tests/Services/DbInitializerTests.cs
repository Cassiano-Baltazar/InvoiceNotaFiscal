using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceApp.Tests.Services;

public class DbInitializerTests : IDisposable
{
    private readonly TestDatabase database = new();

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(database.Connection));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Seeds_company_and_clients_once()
    {
        using var provider = BuildProvider();
        var contentRoot = Path.Combine(Path.GetTempPath(), "Empresa", "InvoiceApp", "src", "InvoiceApp.Web");

        await DbInitializer.InitializeAsync(provider, contentRoot);
        await DbInitializer.InitializeAsync(provider, contentRoot);

        using var db = database.CreateContext();
        var company = await db.CompanySettings.SingleAsync();
        Assert.Equal("YOUR COMPANY", company.TradeName);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Empresa"), company.OutputBasePath);
        Assert.Equal(2, await db.Clients.CountAsync());
        Assert.All(await db.Clients.ToListAsync(), c => Assert.True(c.IsActive));
    }

    public void Dispose() => database.Dispose();
}
