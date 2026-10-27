using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Tests.Infrastructure;

/// <summary>
/// SQLite in-memory database that lives while the instance is alive.
/// Uses the same provider as the app, so constraints and indexes behave the same way.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public SqliteConnection Connection { get; }

    public TestDatabase()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public DbContextOptions<AppDbContext> Options =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options;

    public AppDbContext CreateContext() => new(Options);

    public CompanySettings SeedCompany(Action<CompanySettings>? configure = null)
    {
        var company = new CompanySettings
        {
            Id = 1,
            LegalName = "Test Company Ltda",
            TradeName = "TESTCO",
            Cnpj = "00.000.000/0001-00",
            AddressLine1 = "Street 1",
            City = "Porto Alegre",
            State = "RS",
            PostalCode = "90000-000",
            Country = "Brazil",
            Phone = "+55 51 0000-0000",
            Email = "test@example.com",
            OutputBasePath = Path.Combine(Path.GetTempPath(), "invoiceapp-tests", Guid.NewGuid().ToString("N")),
            IssueDay = 1,
            PaymentTermsDays = 15,
            ServiceDescriptionTemplate = "Software development services — {serviceMonth}"
        };
        configure?.Invoke(company);

        using var db = CreateContext();
        db.CompanySettings.Add(company);
        db.SaveChanges();
        return company;
    }

    public Client SeedClient(string name = "Client A", bool isActive = true, decimal amount = 1000m, string currency = "CAD")
    {
        var client = new Client
        {
            Name = name,
            AddressLine1 = "1 Main St",
            City = "Montreal",
            Region = "QC",
            PostalCode = "H0H 0H0",
            Country = "Canada",
            Currency = currency,
            CurrencyLabel = currency + " label",
            MonthlyAmount = amount,
            WisePaymentLink = "https://wise.com/pay/test",
            IsActive = isActive
        };

        using var db = CreateContext();
        db.Clients.Add(client);
        db.SaveChanges();
        return client;
    }

    public Invoice SeedInvoice(
        int clientId,
        InvoiceStatus status = InvoiceStatus.Generated,
        string? number = null,
        int issueYear = 2026,
        int issueMonth = 10,
        string? pdfPath = null)
    {
        var serviceDate = new DateOnly(issueYear, issueMonth, 1).AddMonths(-1);
        using var db = CreateContext();
        var sequence = db.Invoices.Count(x => x.IssueYear == issueYear && x.IssueMonth == issueMonth) + 1;
        var client = db.Clients.Find(clientId)!;

        var invoice = new Invoice
        {
            InvoiceNumber = number ?? $"INV-{issueYear}-{issueMonth:D2}-{sequence:D3}",
            ClientId = clientId,
            ClientName = client.Name,
            ClientCountry = client.Country,
            ServiceYear = serviceDate.Year,
            ServiceMonth = serviceDate.Month,
            IssueYear = issueYear,
            IssueMonth = issueMonth,
            InvoiceDate = new DateOnly(issueYear, issueMonth, 1),
            DueDate = new DateOnly(issueYear, issueMonth, 16),
            Currency = client.Currency,
            CurrencyLabel = client.CurrencyLabel,
            Subtotal = client.MonthlyAmount,
            Tax = 0,
            Total = client.MonthlyAmount,
            WisePaymentLink = client.WisePaymentLink,
            ServiceDescription = "Services",
            Status = status,
            PdfPath = pdfPath,
            Items =
            [
                new InvoiceItem
                {
                    Position = 1,
                    Description = "Services",
                    Quantity = 1,
                    Unit = "month",
                    UnitPrice = client.MonthlyAmount,
                    Amount = client.MonthlyAmount
                }
            ]
        };

        db.Invoices.Add(invoice);
        db.SaveChanges();
        return invoice;
    }

    public Invoice GetInvoice(int id)
    {
        using var db = CreateContext();
        return db.Invoices.AsNoTracking().Include(x => x.Items).Single(x => x.Id == id);
    }

    public void Dispose() => Connection.Dispose();
}
