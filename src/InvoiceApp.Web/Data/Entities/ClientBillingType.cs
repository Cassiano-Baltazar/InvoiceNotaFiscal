namespace InvoiceApp.Web.Data.Entities;

/// <summary>How a client is billed.</summary>
public enum ClientBillingType
{
    /// <summary>Fixed amount every month: included in "Generate Month".</summary>
    Monthly = 1,

    /// <summary>Billed per job: not included in "Generate Month"; each invoice is created by hand with its own amount.</summary>
    Freelance = 2
}
