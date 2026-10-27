using Bunit;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Components.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using SettingsIndex = InvoiceApp.Web.Components.Pages.Settings.Index;

namespace InvoiceApp.Tests.Components;

public class CertificateSettingsTests : ComponentTestBase
{
    private static void Upload(IRenderedComponent<CertificateSettings> cut, byte[] pfx, string name = "empresa.pfx") =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(pfx, name));

    [Fact]
    public void Settings_page_shows_the_certificate_section()
    {
        Database.SeedCompany();

        var cut = Render<SettingsIndex>();

        Assert.Contains("Digital Certificate (NFS-e)", cut.Markup);
        Assert.NotNull(cut.Find("[data-field=no-certificate]"));
    }

    [Fact]
    public void Save_is_disabled_until_a_file_is_selected()
    {
        var cut = Render<CertificateSettings>();

        Assert.True(cut.Find("[data-action=save-certificate]").HasAttribute("disabled"));
    }

    [Fact]
    public void Uploading_a_certificate_with_its_password_stores_it_and_shows_details()
    {
        var cut = Render<CertificateSettings>();

        Upload(cut, TestCertificates.Pfx("MY COMPANY LTDA:11222333000144"));
        cut.Find("[data-field=certificate-password]").Change(TestCertificates.Password);
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-action=save-certificate]").HasAttribute("disabled")));
        cut.Find("[data-action=save-certificate]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Certificate saved.", cut.Find(".message").TextContent));
        Assert.Contains("MY COMPANY LTDA:11222333000144", cut.Find("[data-field=certificate-info]").TextContent);
        Assert.Contains("empresa.pfx", cut.Markup);
        using var db = Database.CreateContext();
        Assert.Single(db.DigitalCertificates.AsNoTracking());
    }

    [Fact]
    public void Wrong_password_shows_an_error()
    {
        var cut = Render<CertificateSettings>();

        Upload(cut, TestCertificates.Pfx());
        cut.Find("[data-field=certificate-password]").Change("wrong");
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-action=save-certificate]").HasAttribute("disabled")));
        cut.Find("[data-action=save-certificate]").Click();

        cut.WaitForAssertion(() => Assert.Contains("message-error", cut.Find(".message").ClassName));
        using var db = Database.CreateContext();
        Assert.Empty(db.DigitalCertificates);
    }

    [Fact]
    public void Certificate_close_to_expiring_shows_days_left()
    {
        var cut = Render<CertificateSettings>();

        Upload(cut, TestCertificates.Pfx(notAfter: DateTimeOffset.UtcNow.AddDays(10.5)));
        cut.Find("[data-field=certificate-password]").Change(TestCertificates.Password);
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-action=save-certificate]").HasAttribute("disabled")));
        cut.Find("[data-action=save-certificate]").Click();

        cut.WaitForAssertion(() => Assert.Contains("10 days left", cut.Find(".status-warning").TextContent));
    }

    [Fact]
    public void Remove_asks_for_confirmation_and_deletes()
    {
        ConfirmDialogsReturn(true);
        var cut = Render<CertificateSettings>();
        Upload(cut, TestCertificates.Pfx());
        cut.Find("[data-field=certificate-password]").Change(TestCertificates.Password);
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-action=save-certificate]").HasAttribute("disabled")));
        cut.Find("[data-action=save-certificate]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-action=remove-certificate]"));

        cut.Find("[data-action=remove-certificate]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Certificate removed.", cut.Find(".message").TextContent));
        using var db = Database.CreateContext();
        Assert.Empty(db.DigitalCertificates);
    }
}
