namespace InvoiceApp.Web.Data.Entities;

/// <summary>
/// The company's ICP-Brasil e-CNPJ A1 certificate (.pfx), used to sign and send NFS-e.
/// The file lives in the database so the app keeps working when the database is moved.
/// The password is encrypted with a key kept outside the database (see <see cref="Services.ISecretProtector"/>).
/// There is at most one row (Id = 1).
/// </summary>
public class DigitalCertificate
{
    public int Id { get; set; } = 1;
    public string FileName { get; set; } = string.Empty;
    public byte[] PfxData { get; set; } = [];
    public string ProtectedPassword { get; set; } = string.Empty;

    // Read from the certificate when it is saved, so the screen does not need to open it.
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
