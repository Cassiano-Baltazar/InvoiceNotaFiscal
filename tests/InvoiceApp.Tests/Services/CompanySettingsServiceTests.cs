using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class CompanySettingsServiceTests : ServiceTestBase
{
    private CompanySettingsService CreateService() => new(NewContext());

    [Fact]
    public async Task Get_creates_default_settings_when_missing()
    {
        var settings = await CreateService().GetAsync();

        Assert.Equal(1, settings.Id);
        Assert.Equal(1, settings.IssueDay);
        Assert.Equal(15, settings.PaymentTermsDays);
        Assert.Single(NewContext().CompanySettings);
    }

    [Fact]
    public async Task Get_returns_existing_settings()
    {
        Database.SeedCompany();

        var settings = await CreateService().GetAsync();

        Assert.Equal("TESTCO", settings.TradeName);
    }

    [Fact]
    public async Task Save_persists_changes()
    {
        var settings = Database.SeedCompany();
        settings.TradeName = "Changed";
        settings.PaymentTermsDays = 30;

        await CreateService().SaveAsync(settings);

        var loaded = await CreateService().GetAsync();
        Assert.Equal("Changed", loaded.TradeName);
        Assert.Equal(30, loaded.PaymentTermsDays);
    }
}
