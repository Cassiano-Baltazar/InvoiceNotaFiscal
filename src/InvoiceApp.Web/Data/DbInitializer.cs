using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, string contentRootPath)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await SchemaUpgrader.UpgradeAsync(db);

        if (await db.CompanySettings.AnyAsync())
            return;

        var outputBasePath = Directory.GetParent(contentRootPath)?.Parent?.Parent?.FullName
            ?? contentRootPath;

        // Placeholder data for a brand-new database. Real company and client data is
        // entered in the Settings and Clients screens and stays only in the local database.
        db.CompanySettings.Add(new CompanySettings
        {
            Id = 1,
            LegalName = "Your Company Ltda",
            TradeName = "YOUR COMPANY",
            Cnpj = "00.000.000/0001-00",
            AddressLine1 = "Street, 000",
            AddressLine2 = "District",
            City = "City",
            State = "State",
            PostalCode = "00000-000",
            Country = "Brazil",
            Phone = "+55 00 00000-0000",
            Email = "billing@example.com",
            OutputBasePath = outputBasePath,
            IssueDay = 1,
            PaymentTermsDays = 15
        });

        db.Clients.AddRange(
            new Client
            {
                Name = "Sample Client Canada Inc.",
                AddressLine1 = "1 Example Street",
                City = "Montreal",
                Region = "QC",
                PostalCode = "H0H 0H0",
                Country = "Canada",
                Currency = "CAD",
                CurrencyLabel = "Canadian Dollars",
                MonthlyAmount = 4000m,
                WisePaymentLink = "https://wise.com/pay/r/your-cad-link",
                IsActive = true
            },
            new Client
            {
                Name = "Sample Client UK Ltd",
                AddressLine1 = "1 Example Road",
                City = "London",
                PostalCode = "EC1A 1AA",
                Country = "United Kingdom",
                Currency = "GBP",
                CurrencyLabel = "British Pounds",
                MonthlyAmount = 1000m,
                WisePaymentLink = "https://wise.com/pay/r/your-gbp-link",
                IsActive = true
            });

        await db.SaveChangesAsync();
    }
}
