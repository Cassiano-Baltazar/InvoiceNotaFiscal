using Bunit;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Components.Pages;
using InvoiceApp.Web.Data.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClientEdit = InvoiceApp.Web.Components.Pages.Clients.Edit;
using ClientIndex = InvoiceApp.Web.Components.Pages.Clients.Index;
using SettingsIndex = InvoiceApp.Web.Components.Pages.Settings.Index;

namespace InvoiceApp.Tests.Components;

public class OtherPagesTests : ComponentTestBase
{
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void Dashboard_shows_counts()
    {
        Database.SeedCompany();
        var client = Database.SeedClient("Alpha");
        Database.SeedClient("Inactive", isActive: false);
        Database.SeedInvoice(client.Id, issueYear: DateTime.Now.Year, issueMonth: DateTime.Now.Month);
        Database.SeedInvoice(client.Id, issueYear: 2020, issueMonth: 1);

        var cut = Render<Home>();

        var values = cut.FindAll(".stat-card strong").Select(x => x.TextContent).ToArray();
        Assert.Equal(["1", "2", "1"], values);
    }

    [Fact]
    public void Clients_list_shows_clients_with_status()
    {
        Database.SeedClient("Alpha");
        Database.SeedClient("Beta", isActive: false);

        var cut = Render<ClientIndex>();

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Active", rows[0].TextContent);
        Assert.Contains("Inactive", rows[1].TextContent);
    }

    [Fact]
    public void Clients_list_empty_message()
    {
        var cut = Render<ClientIndex>();

        Assert.Contains("No clients registered yet.", cut.Markup);
    }

    [Fact]
    public void New_client_is_saved_and_navigates_back()
    {
        var cut = Render<ClientEdit>();
        Assert.Contains("New Client", cut.Find("h1").TextContent);

        var inputs = cut.FindAll("input");
        inputs[0].Change("Gamma");
        cut.FindAll("input")[7].Change("Germany");
        cut.FindAll("input")[8].Change("EUR");
        cut.FindAll("input")[10].Change("1234.5");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith("/clients", Navigation.Uri));
        using var db = Database.CreateContext();
        var saved = db.Clients.Single();
        Assert.Equal("Gamma", saved.Name);
        Assert.Equal("EUR", saved.Currency);
        Assert.Equal(1234.5m, saved.MonthlyAmount);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public void Client_nfse_fields_are_saved()
    {
        var client = Database.SeedClient("Alpha");
        var cut = Render<ClientEdit>(p => p.Add(x => x.Id, client.Id));

        cut.Find("[data-field=country-code]").Change("ca");
        cut.Find("[data-field=tax-id]").Change("123456789RC0001");
        cut.Find("form").Submit();

        using var db = Database.CreateContext();
        var saved = db.Clients.AsNoTracking().Single();
        Assert.Equal("CA", saved.CountryCode);
        Assert.Equal("123456789RC0001", saved.TaxId);
    }

    [Fact]
    public void Existing_client_is_loaded_and_updated()
    {
        var client = Database.SeedClient("Alpha");
        var cut = Render<ClientEdit>(p => p.Add(x => x.Id, client.Id));

        Assert.Contains("Edit Client", cut.Find("h1").TextContent);
        Assert.Equal("Alpha", cut.FindAll("input")[0].GetAttribute("value"));
        cut.FindAll("input")[0].Change("Alpha Renamed");
        cut.Find("form").Submit();

        using var db = Database.CreateContext();
        Assert.Equal("Alpha Renamed", db.Clients.AsNoTracking().Single().Name);
    }

    [Fact]
    public void Nfse_settings_are_saved()
    {
        Database.SeedCompany();
        var cut = Render<SettingsIndex>();

        cut.Find("[data-field=municipality-code]").Change("4309308");
        cut.Find("[data-field=simples-rate]").Change("6.5");
        cut.Find("[data-field=nfse-environment]").Change(nameof(NfseEnvironment.Production));
        cut.Find("form").Submit();

        using var db = Database.CreateContext();
        var saved = db.CompanySettings.AsNoTracking().Single();
        Assert.Equal("4309308", saved.MunicipalityCode);
        Assert.Equal(6.5m, saved.SimplesNacionalTaxRate);
        Assert.Equal(NfseEnvironment.Production, saved.NfseEnvironment);
        Assert.Equal("1", saved.DpsSeries);
        Assert.Equal(1, saved.NextDpsNumber);
    }

    [Fact]
    public void Settings_are_loaded_and_saved()
    {
        Database.SeedCompany();
        var cut = Render<SettingsIndex>();

        var tradeName = cut.FindAll("input").First(i => i.GetAttribute("value") == "TESTCO");
        tradeName.Change("NEW NAME");
        cut.Find("form").Submit();

        using var db = Database.CreateContext();
        Assert.Equal("NEW NAME", db.CompanySettings.AsNoTracking().Single().TradeName);
    }
}
