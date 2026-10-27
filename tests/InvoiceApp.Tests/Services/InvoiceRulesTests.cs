using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public class InvoiceRulesTests
{
    [Theory]
    [InlineData(InvoiceStatus.Generated, true)]
    [InlineData(InvoiceStatus.Sent, true)]
    [InlineData(InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    public void CanEdit(InvoiceStatus status, bool expected) =>
        Assert.Equal(expected, InvoiceRules.CanEdit(status));

    [Theory]
    [InlineData(InvoiceStatus.Generated, true)]
    [InlineData(InvoiceStatus.Sent, false)]
    [InlineData(InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    public void CanDelete(InvoiceStatus status, bool expected) =>
        Assert.Equal(expected, InvoiceRules.CanDelete(status));

    [Theory]
    [InlineData(InvoiceStatus.Generated, true)]
    [InlineData(InvoiceStatus.Sent, true)]
    [InlineData(InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    public void CanCancel(InvoiceStatus status, bool expected) =>
        Assert.Equal(expected, InvoiceRules.CanCancel(status));

    [Theory]
    [InlineData(InvoiceStatus.Generated, false)]
    [InlineData(InvoiceStatus.Sent, false)]
    [InlineData(InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Cancelled, true)]
    public void CanReactivate(InvoiceStatus status, bool expected) =>
        Assert.Equal(expected, InvoiceRules.CanReactivate(status));

    [Theory]
    [InlineData(InvoiceStatus.Generated, true)]
    [InlineData(InvoiceStatus.Sent, false)]
    [InlineData(InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    public void CanMarkAsSent(InvoiceStatus status, bool expected) =>
        Assert.Equal(expected, InvoiceRules.CanMarkAsSent(status));

    [Theory]
    [InlineData(InvoiceStatus.Generated, true)]
    [InlineData(InvoiceStatus.Sent, true)]
    [InlineData(InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    public void CanMarkAsPaid(InvoiceStatus status, bool expected) =>
        Assert.Equal(expected, InvoiceRules.CanMarkAsPaid(status));

    [Fact]
    public void Paid_invoice_allows_no_action()
    {
        const InvoiceStatus paid = InvoiceStatus.Paid;
        Assert.False(InvoiceRules.CanEdit(paid));
        Assert.False(InvoiceRules.CanDelete(paid));
        Assert.False(InvoiceRules.CanCancel(paid));
        Assert.False(InvoiceRules.CanReactivate(paid));
        Assert.False(InvoiceRules.CanMarkAsSent(paid));
        Assert.False(InvoiceRules.CanMarkAsPaid(paid));
    }

    [Theory]
    [InlineData(InvoiceStatus.Generated, "Not Sent")]
    [InlineData(InvoiceStatus.Sent, "Sent")]
    [InlineData(InvoiceStatus.Paid, "Paid")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelled")]
    public void GetLabel(InvoiceStatus status, string expected) =>
        Assert.Equal(expected, InvoiceRules.GetLabel(status));
}
