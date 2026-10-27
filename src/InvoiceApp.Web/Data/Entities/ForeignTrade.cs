namespace InvoiceApp.Web.Data.Entities;

/// <summary>Why a foreign taker has no tax id (NIF). Same codes as the national NFS-e (cNaoNIF).</summary>
public enum MissingTaxIdReason
{
    NotInformed = 0,
    Exempt = 1,
    NotRequired = 2
}

/// <summary>How the exported service is provided. Same codes as the national NFS-e (mdPrestacao).</summary>
public enum ForeignServiceMode
{
    Unknown = 0,
    CrossBorder = 1,
    ConsumptionInBrazil = 2,
    CommercialPresenceAbroad = 3,
    TemporaryPresenceOfPersons = 4
}
