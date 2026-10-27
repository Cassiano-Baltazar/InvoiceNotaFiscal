using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class ClientServiceTests : ServiceTestBase
{
    private ClientService CreateService() => new(NewContext());

    [Fact]
    public async Task Get_all_is_ordered_by_name()
    {
        Database.SeedClient("Zeta");
        Database.SeedClient("Alpha", isActive: false);

        var clients = await CreateService().GetAllAsync();

        Assert.Equal(["Alpha", "Zeta"], clients.Select(x => x.Name));
    }

    [Fact]
    public async Task Save_new_client_inserts_it()
    {
        var saved = await CreateService().SaveAsync(new Client { Name = "New", Currency = "EUR", Country = "Germany", MonthlyAmount = 10 });

        Assert.True(saved.Id > 0);
        var loaded = await CreateService().GetAsync(saved.Id);
        Assert.Equal("New", loaded!.Name);
        Assert.True(loaded.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Save_existing_client_updates_it()
    {
        var client = Database.SeedClient("Old");
        client.Name = "Renamed";
        client.MonthlyAmount = 2500m;

        await CreateService().SaveAsync(client);

        var loaded = await CreateService().GetAsync(client.Id);
        Assert.Equal("Renamed", loaded!.Name);
        Assert.Equal(2500m, loaded.MonthlyAmount);
    }

    [Fact]
    public async Task Get_returns_null_for_missing_client() =>
        Assert.Null(await CreateService().GetAsync(999));

    [Fact]
    public async Task Delete_removes_client_without_invoices()
    {
        var client = Database.SeedClient();

        await CreateService().DeleteAsync(client.Id);

        Assert.Null(await CreateService().GetAsync(client.Id));
    }

    [Fact]
    public async Task Delete_only_deactivates_client_with_invoices()
    {
        Database.SeedCompany();
        var client = Database.SeedClient();
        Database.SeedInvoice(client.Id);

        await CreateService().DeleteAsync(client.Id);

        var loaded = await CreateService().GetAsync(client.Id);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsActive);
    }

    [Fact]
    public async Task Delete_missing_client_does_nothing() =>
        await CreateService().DeleteAsync(999);
}
