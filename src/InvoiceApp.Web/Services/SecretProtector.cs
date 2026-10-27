using System.Security.Cryptography;
using System.Text;

namespace InvoiceApp.Web.Services;

/// <summary>Encrypts small secrets (the certificate password) before they go to the database.</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    /// <summary>Returns null when the secret cannot be decrypted, e.g. the key file is missing or different.</summary>
    string? Unprotect(string protectedText);
}

/// <summary>
/// AES-GCM with a 256-bit key stored in a file outside the database (by default App_Data/certificate.key).
/// A copy of the database alone does not reveal the password. To move the app to another machine,
/// copy the key file together with the database; without it the certificate has to be uploaded again.
/// </summary>
public sealed class AesSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] key;

    public AesSecretProtector(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("The key must have 32 bytes.", nameof(key));
        this.key = key;
    }

    /// <summary>Reads the key from the file, creating a new random key when the file does not exist.</summary>
    public static AesSecretProtector FromKeyFile(string path)
    {
        if (File.Exists(path))
            return new AesSecretProtector(Convert.FromBase64String(File.ReadAllText(path).Trim()));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(path, Convert.ToBase64String(key));
        return new AesSecretProtector(key);
    }

    public string Protect(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return Convert.ToBase64String(output);
    }

    public string? Unprotect(string protectedText)
    {
        try
        {
            var input = Convert.FromBase64String(protectedText);
            if (input.Length < NonceSize + TagSize)
                return null;

            var plain = new byte[input.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
