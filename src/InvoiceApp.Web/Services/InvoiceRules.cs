using InvoiceApp.Web.Data.Entities;
using static InvoiceApp.Web.Services.Ui;

namespace InvoiceApp.Web.Services;

/// <summary>
/// Which actions are allowed for each invoice status.
/// Generated (not sent): every action except reactivate.
/// Sent: edit (goes back to Generated), cancel and mark as paid. Cannot be deleted.
/// Paid: final state, nothing can be changed.
/// Cancelled: can only be reactivated (goes back to Generated).
/// </summary>
public static class InvoiceRules
{
    public static bool CanEdit(InvoiceStatus status) =>
        status is InvoiceStatus.Generated or InvoiceStatus.Sent;

    public static bool CanDelete(InvoiceStatus status) =>
        status is InvoiceStatus.Generated;

    public static bool CanCancel(InvoiceStatus status) =>
        status is InvoiceStatus.Generated or InvoiceStatus.Sent;

    public static bool CanReactivate(InvoiceStatus status) =>
        status is InvoiceStatus.Cancelled;

    public static bool CanMarkAsSent(InvoiceStatus status) =>
        status is InvoiceStatus.Generated;

    public static bool CanMarkAsPaid(InvoiceStatus status) =>
        status is InvoiceStatus.Generated or InvoiceStatus.Sent;

    public static string GetLabel(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Generated => T("Not Sent"),
        InvoiceStatus.Sent => T("Sent"),
        InvoiceStatus.Paid => T("Paid"),
        InvoiceStatus.Cancelled => T("Cancelled"),
        _ => status.ToString()
    };
}
