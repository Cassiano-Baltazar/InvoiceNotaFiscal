using Bunit;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using WithdrawalIndex = InvoiceApp.Web.Components.Pages.Withdrawal.Index;

namespace InvoiceApp.Tests.Components;

public class WithdrawalPageTests : ComponentTestBase
{
    [Fact]
    public void Shows_the_withdrawal_of_the_month_and_saves_the_guide()
    {
        Database.SeedCompany(x =>
        {
            x.CompanyStartDate = new DateOnly(2026, 8, 25);
            x.AccountantMonthlyFee = 257m;
        });
        var invoice = Database.SeedInvoice(Database.SeedClient().Id);
        using (var db = Database.CreateContext())
        {
            db.Nfses.Add(new Nfse
            {
                InvoiceId = invoice.Id,
                Status = NfseStatus.Issued,
                Number = "1",
                Series = "1",
                IssuedAt = new DateTime(2026, 11, 4, 10, 0, 0),
                TakerName = "Client",
                Description = "Services",
                AmountBrl = 15_487.82m
            });
            db.SaveChanges();
        }

        var cut = Render<WithdrawalIndex>();
        cut.Find("[data-field=month]").Change("2026-11");

        // First month with revenue: RBT12 = 15.487,82 × 12, bracket 2.
        cut.WaitForAssertion(() => Assert.Contains("15.487,82", cut.Find("[data-stat=revenue]").TextContent));
        Assert.Contains("Anexo III, bracket 2", cut.Find("[data-field=das-detail]").TextContent);
        Assert.Contains("estimated", cut.Find("[data-row=das]").TextContent);

        cut.Find("[data-field=das-guide]").Change("472.38");
        cut.Find("[data-action=save-month]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Month saved.", cut.Markup));
        Assert.Contains("472,38", cut.Find("[data-row=das]").TextContent);
        // 15.487,82 − 472,38 − 477,02 (INSS) = 14.538,42; the accountant is paid from it.
        Assert.Contains("14.281,42", cut.Find("[data-stat=withdrawal]").TextContent);
    }
}
