using InvoiceApp.Web.Data.Entities;
using static InvoiceApp.Web.Services.Ui;

namespace InvoiceApp.Web.Services;

public static class ClientLabels
{
    public static string BillingTypeLabel(ClientBillingType type) => type switch
    {
        ClientBillingType.Freelance => T("Freelance (per job)"),
        _ => T("Monthly")
    };
}
