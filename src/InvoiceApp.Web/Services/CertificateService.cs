using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Services;

public record CertificateInfo(
    string FileName,
    string Subject,
    string Issuer,
    string Thumbprint,
    DateTime ValidFrom,
    DateTime ValidUntil,
    DateTime UploadedAt,
    bool PasswordReadable)
{
    public bool IsExpired(DateTime now) => now > ValidUntil;
    public int DaysLeft(DateTime now) => (int)Math.Floor((ValidUntil - now).TotalDays);
}

public interface ICertificateService
{
    Task<CertificateInfo?> GetInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks the file and password and replaces the stored certificate. Throws <see cref="InvalidOperationException"/> with a readable message when invalid.</summary>
    Task<CertificateInfo> SaveAsync(byte[] pfxData, string fileName, string password, CancellationToken cancellationToken = default);

    /// <summary>Opens the stored certificate with its private key, or null when none is stored.</summary>
    Task<X509Certificate2?> LoadAsync(CancellationToken cancellationToken = default);

    Task RemoveAsync(CancellationToken cancellationToken = default);
}

public class CertificateService(AppDbContext db, ISecretProtector protector) : ICertificateService
{
    public const long MaxFileSize = 1024 * 1024;

    public async Task<CertificateInfo?> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.DigitalCertificates.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return stored is null ? null : ToInfo(stored);
    }

    public async Task<CertificateInfo> SaveAsync(byte[] pfxData, string fileName, string password, CancellationToken cancellationToken = default)
    {
        if (pfxData.Length == 0)
            throw new InvalidOperationException("The certificate file is empty.");
        if (pfxData.Length > MaxFileSize)
            throw new InvalidOperationException("The certificate file is too large. Select the .pfx (or .p12) file of an A1 certificate.");

        using var certificate = Open(pfxData, password, X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("The file does not contain the private key. Export the certificate as .pfx including the private key.");

        var stored = await db.DigitalCertificates.FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            stored = new DigitalCertificate();
            db.DigitalCertificates.Add(stored);
        }

        stored.FileName = Path.GetFileName(fileName);
        stored.PfxData = pfxData;
        stored.ProtectedPassword = protector.Protect(password);
        stored.Subject = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        stored.Issuer = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
        stored.Thumbprint = certificate.Thumbprint;
        stored.ValidFrom = certificate.NotBefore.ToUniversalTime();
        stored.ValidUntil = certificate.NotAfter.ToUniversalTime();
        stored.UploadedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToInfo(stored);
    }

    public async Task<X509Certificate2?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.DigitalCertificates.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
            return null;

        var password = protector.Unprotect(stored.ProtectedPassword)
            ?? throw new InvalidOperationException(PasswordUnreadableMessage);
        return Open(stored.PfxData, password, X509KeyStorageFlags.Exportable);
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        await db.DigitalCertificates.ExecuteDeleteAsync(cancellationToken);
    }

    public const string PasswordUnreadableMessage =
        "The certificate password could not be decrypted on this machine (the key file is missing or different). Upload the certificate again.";

    private CertificateInfo ToInfo(DigitalCertificate stored) => new(
        stored.FileName,
        stored.Subject,
        stored.Issuer,
        stored.Thumbprint,
        DateTime.SpecifyKind(stored.ValidFrom, DateTimeKind.Utc),
        DateTime.SpecifyKind(stored.ValidUntil, DateTimeKind.Utc),
        DateTime.SpecifyKind(stored.UploadedAt, DateTimeKind.Utc),
        protector.Unprotect(stored.ProtectedPassword) is not null);

    private static X509Certificate2 Open(byte[] pfxData, string password, X509KeyStorageFlags flags)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfxData, password, flags);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Could not open the certificate. Check the password and that the file is a .pfx (or .p12).");
        }
    }
}
