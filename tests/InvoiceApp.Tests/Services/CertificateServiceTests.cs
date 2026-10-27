using System.Security.Cryptography;
using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Tests.Services;

public class CertificateServiceTests : ServiceTestBase
{
    private readonly AesSecretProtector protector = new(RandomNumberGenerator.GetBytes(32));

    private CertificateService CreateService(ISecretProtector? withProtector = null) => new(NewContext(), withProtector ?? protector);

    [Fact]
    public async Task No_certificate_by_default()
    {
        Assert.Null(await CreateService().GetInfoAsync());
        Assert.Null(await CreateService().LoadAsync());
    }

    [Fact]
    public async Task Save_stores_the_file_and_reads_its_details()
    {
        var until = new DateTimeOffset(2027, 5, 10, 12, 0, 0, TimeSpan.Zero);
        var pfx = TestCertificates.Pfx(notAfter: until);

        var info = await CreateService().SaveAsync(pfx, "certs/empresa.pfx", TestCertificates.Password);

        Assert.Equal("empresa.pfx", info.FileName);
        Assert.Equal("TEST COMPANY LTDA:11222333000144", info.Subject);
        Assert.Equal(until.UtcDateTime, info.ValidUntil);
        Assert.True(info.PasswordReadable);

        var reloaded = await CreateService().GetInfoAsync();
        Assert.Equal(info, reloaded);
        using var db = NewContext();
        Assert.Equal(pfx, (await db.DigitalCertificates.SingleAsync()).PfxData);
    }

    [Fact]
    public async Task Password_is_not_stored_in_plain_text()
    {
        await CreateService().SaveAsync(TestCertificates.Pfx(), "a.pfx", TestCertificates.Password);

        using var db = NewContext();
        var stored = await db.DigitalCertificates.SingleAsync();
        Assert.DoesNotContain(TestCertificates.Password, stored.ProtectedPassword);
        Assert.Equal(TestCertificates.Password, protector.Unprotect(stored.ProtectedPassword));
    }

    [Fact]
    public async Task Load_opens_the_certificate_with_its_private_key()
    {
        var info = await CreateService().SaveAsync(TestCertificates.Pfx(), "a.pfx", TestCertificates.Password);

        using var certificate = await CreateService().LoadAsync();

        Assert.NotNull(certificate);
        Assert.True(certificate.HasPrivateKey);
        Assert.Equal(info.Thumbprint, certificate.Thumbprint);
    }

    [Fact]
    public async Task Saving_again_replaces_the_certificate()
    {
        await CreateService().SaveAsync(TestCertificates.Pfx("OLD"), "old.pfx", TestCertificates.Password);
        await CreateService().SaveAsync(TestCertificates.Pfx("NEW", password: "other"), "new.pfx", "other");

        using var db = NewContext();
        var stored = await db.DigitalCertificates.SingleAsync();
        Assert.Equal("NEW", stored.Subject);
        Assert.Equal("new.pfx", stored.FileName);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_and_nothing_is_stored()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().SaveAsync(TestCertificates.Pfx(), "a.pfx", "wrong"));

        Assert.Contains("password", ex.Message);
        Assert.Null(await CreateService().GetInfoAsync());
    }

    [Fact]
    public async Task File_that_is_not_a_certificate_is_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().SaveAsync("not a pfx"u8.ToArray(), "a.pfx", "x"));
    }

    [Fact]
    public async Task Certificate_without_private_key_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().SaveAsync(TestCertificates.Pfx(withPrivateKey: false), "a.pfx", TestCertificates.Password));

        Assert.Contains("private key", ex.Message);
    }

    [Fact]
    public async Task Database_moved_without_the_key_reports_the_password_as_unreadable()
    {
        await CreateService().SaveAsync(TestCertificates.Pfx(), "a.pfx", TestCertificates.Password);
        var otherMachine = new AesSecretProtector(RandomNumberGenerator.GetBytes(32));

        var info = await CreateService(otherMachine).GetInfoAsync();

        Assert.NotNull(info);
        Assert.False(info.PasswordReadable);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(otherMachine).LoadAsync());
        Assert.Equal(CertificateService.PasswordUnreadableMessage, ex.Message);
    }

    [Fact]
    public async Task Database_moved_with_the_key_file_keeps_working()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), "invoiceapp-tests", Guid.NewGuid().ToString("N"), "certificate.key");
        await CreateService(AesSecretProtector.FromKeyFile(keyPath)).SaveAsync(TestCertificates.Pfx(), "a.pfx", TestCertificates.Password);

        using var certificate = await CreateService(AesSecretProtector.FromKeyFile(keyPath)).LoadAsync();

        Assert.NotNull(certificate);
        Assert.True(certificate.HasPrivateKey);
    }

    [Fact]
    public async Task Remove_deletes_the_certificate()
    {
        await CreateService().SaveAsync(TestCertificates.Pfx(), "a.pfx", TestCertificates.Password);

        await CreateService().RemoveAsync();

        Assert.Null(await CreateService().GetInfoAsync());
    }
}
