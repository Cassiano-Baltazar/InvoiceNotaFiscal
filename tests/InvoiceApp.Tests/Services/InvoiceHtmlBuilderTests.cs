using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class InvoiceHtmlBuilderTests
{
    internal static InvoiceReportModel SampleModel() => new()
    {
        Company = new CompanySettings
        {
            LegalName = "Test Company Ltda",
            TradeName = "TESTCO",
            Cnpj = "11.222.333/0001-44",
            AddressLine1 = "Street 1",
            City = "Porto Alegre",
            State = "RS",
            Country = "Brazil",
            Email = "a@b.com"
        },
        Invoice = new Invoice
        {
            InvoiceNumber = "INV-2026-10-001",
            ClientName = "Client <A> & Co",
            ClientCountry = "Canada",
            ServiceYear = 2026,
            ServiceMonth = 9,
            IssueYear = 2026,
            IssueMonth = 10,
            InvoiceDate = new DateOnly(2026, 10, 1),
            DueDate = new DateOnly(2026, 10, 16),
            Currency = "CAD",
            CurrencyLabel = "Canadian Dollars",
            Subtotal = 4000m,
            Total = 4000m,
            ServiceDescription = "Software development services — September 2026",
            WisePaymentLink = "https://wise.com/pay/test",
            Items =
            [
                new InvoiceItem { Position = 2, Description = "Extra <b>hours</b>", Quantity = 2.5m, Unit = "hour", UnitPrice = 40m, Amount = 100m },
                new InvoiceItem { Position = 1, Description = "Monthly retainer", Quantity = 1, Unit = "month", UnitPrice = 3900m, Amount = 3900m }
            ]
        }
    };

    [Fact]
    public void Report_model_formats_dates_and_amounts()
    {
        var model = SampleModel();

        Assert.Equal("October 1, 2026", model.FormattedInvoiceDate);
        Assert.Equal("October 16, 2026", model.FormattedDueDate);
        Assert.Equal("4,000.00", model.FormattedSubtotal);
        Assert.Equal("0.00", model.FormattedTax);
        Assert.Equal("4,000.00", model.FormattedTotal);
        Assert.Equal(["Monthly retainer", "Extra <b>hours</b>"], model.Items.Select(x => x.Description));
        Assert.Equal("2.5 hour", InvoiceReportModel.FormatQuantity(model.Items[1]));
        Assert.Equal("3", InvoiceReportModel.FormatQuantity(new InvoiceItem { Quantity = 3 }));
    }

    [Fact]
    public void Html_contains_invoice_data_and_encodes_user_text()
    {
        var html = InvoiceHtmlBuilder.Build(SampleModel());

        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
        Assert.Contains("INV-2026-10-001", html);
        Assert.Contains("TESTCO", html);
        Assert.Contains("11.222.333/0001-44", html);
        Assert.Contains("4,000.00", html);
        Assert.Contains("https://wise.com/pay/test", html);
        Assert.Contains("Client &lt;A&gt; &amp; Co", html);
        Assert.DoesNotContain("Client <A>", html);
        Assert.Contains("Monthly retainer", html);
        Assert.Contains("Extra &lt;b&gt;hours&lt;/b&gt;", html);
        Assert.Contains("2.5 hour", html);
        Assert.Contains("3,900.00", html);
        Assert.True(html.IndexOf("Monthly retainer") < html.IndexOf("Extra &lt;b&gt;"), "items must be in position order");
    }
}
