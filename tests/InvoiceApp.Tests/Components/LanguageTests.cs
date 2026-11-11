using System.Net;
using Bunit;
using InvoiceApp.Tests.Api;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Components.Pages;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SettingsIndex = InvoiceApp.Web.Components.Pages.Settings.Index;

namespace InvoiceApp.Tests.Components;

/// <summary>The language is global to the app, so these tests run alone, never next to the other tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "Language";
}

[Collection(LanguageCollection.Name)]
public sealed class LanguagePageTests : ComponentTestBase
{
    public LanguagePageTests() => Ui.Language = Ui.English;

    protected override void Dispose(bool disposing)
    {
        Ui.Language = Ui.English;
        base.Dispose(disposing);
    }

    [Fact]
    public void Screens_are_shown_in_portuguese()
    {
        Ui.Language = Ui.Portuguese;

        var cut = Render<Home>();

        Assert.Equal("Painel", cut.Find("h1").TextContent);
        Assert.Contains("Clientes ativos", cut.Markup);
        Assert.Contains("Gerar invoices", cut.Markup);
    }

    [Fact]
    public void Choosing_a_language_in_settings_saves_it_and_reloads_the_app()
    {
        Database.SeedCompany();
        var cut = Render<SettingsIndex>();

        cut.Find("[data-field=language]").Change(Ui.Portuguese);
        cut.Find("form").Submit();

        using var db = Database.CreateContext();
        Assert.Equal(Ui.Portuguese, db.CompanySettings.AsNoTracking().Single().Language);
        Assert.Equal(Ui.Portuguese, Ui.Language);
        Assert.EndsWith("/settings", Services.GetRequiredService<NavigationManager>().Uri);
    }
}

[Collection(LanguageCollection.Name)]
public sealed class LanguageEndpointTests : IClassFixture<AppFactory>, IDisposable
{
    private readonly AppFactory factory;
    private readonly HttpClient client;

    public LanguageEndpointTests(AppFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Ui.Language = Ui.English;
    }

    public void Dispose() => Ui.Language = Ui.English;

    private string SavedLanguage()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().CompanySettings.AsNoTracking().Single().Language;
    }

    [Fact]
    public async Task Sidebar_links_switch_the_language_and_go_back_to_the_page()
    {
        var page = await client.GetStringAsync("/clients");
        Assert.Contains("<html lang=\"en\">", page);
        Assert.Contains("href=\"/language/pt?returnUrl=%2Fclients\"", page);

        var response = await client.GetAsync("/language/pt?returnUrl=%2Fclients");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/clients", response.Headers.Location?.OriginalString);
        Assert.Equal(Ui.Portuguese, SavedLanguage());

        page = WebUtility.HtmlDecode(await client.GetStringAsync("/clients"));
        Assert.Contains("<html lang=\"pt-BR\">", page);
        Assert.Contains("Configurações", page);
        Assert.Contains("Retirada mensal", page);

        response = await client.GetAsync("/language/en?returnUrl=%2F");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Ui.English, SavedLanguage());
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    public async Task Return_url_outside_the_app_goes_to_the_dashboard(string returnUrl)
    {
        var response = await client.GetAsync($"/language/pt?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Unknown_language_is_not_found()
    {
        var response = await client.GetAsync("/language/fr");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Ui.English, Ui.Language);
    }
}
