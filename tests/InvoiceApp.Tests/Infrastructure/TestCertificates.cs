using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace InvoiceApp.Tests.Infrastructure;

/// <summary>Self-signed certificates exported as .pfx, standing in for an e-CNPJ A1.</summary>
public static class TestCertificates
{
    public const string Password = "secret-123";

    public static byte[] Pfx(
        string holder = "TEST COMPANY LTDA:11222333000144",
        string password = Password,
        DateTimeOffset? notAfter = null,
        bool withPrivateKey = true)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={holder}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var until = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);
        using var certificate = request.CreateSelfSigned(until.AddYears(-1), until);

        if (withPrivateKey)
            return certificate.Export(X509ContentType.Pkcs12, password);

        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        return publicOnly.Export(X509ContentType.Pkcs12, password);
    }
}
