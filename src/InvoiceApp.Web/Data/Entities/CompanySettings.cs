namespace InvoiceApp.Web.Data.Entities;

public class CompanySettings
{
    public int Id { get; set; } = 1;
    public string LegalName { get; set; } = string.Empty;
    public string TradeName { get; set; } = string.Empty;
    public string Cnpj { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string OutputBasePath { get; set; } = string.Empty;
    public int IssueDay { get; set; } = 1;
    public int PaymentTermsDays { get; set; } = 15;
    public string ServiceDescriptionTemplate { get; set; } = "Software development services — {serviceMonth}";

    // NFS-e issued through the national API (Emissor Nacional). The app supports Simples Nacional ME/EPP.
    public NfseEnvironment NfseEnvironment { get; set; } = NfseEnvironment.RestrictedProduction;
    public string MunicipalityCode { get; set; } = string.Empty;
    public string DpsSeries { get; set; } = "1";
    public long NextDpsNumber { get; set; } = 1;
    public string DefaultNationalTaxCode { get; set; } = "010101";
    public decimal SimplesNacionalTaxRate { get; set; }
    public string? MunicipalRegistration { get; set; }
}

/// <summary>Environment of the national NFS-e API. Values are the tpAmb codes of the DPS.</summary>
public enum NfseEnvironment
{
    Production = 1,
    RestrictedProduction = 2
}
