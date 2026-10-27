using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Tests.Services;

/// <summary>How invoices and NFS-e work together.</summary>
public class InvoiceNfseIntegrationTests : ServiceTestBase
{
    private readonly Client client;

    public InvoiceNfseIntegrationTests()
    {
        Database.SeedCompany();
        client = Database.SeedClient();
    }

    [Theory]
    [InlineData(InvoiceStatus.Generated)]
    [InlineData(InvoiceStatus.Sent)]
    public async Task Marking_as_paid_creates_a_linked_nfse_draft(InvoiceStatus status)
    {
        var invoice = Database.SeedInvoice(client.Id, status);

        var result = await CreateInvoiceService().MarkAsPaidAsync(invoice.Id);

        Assert.True(result.Success);
        Assert.Contains("marked as paid", result.Message);
        Assert.Contains("NFS-e draft created", result.Message);
        using var db = Database.CreateContext();
        var draft = await db.Nfses.SingleAsync();
        Assert.Equal(invoice.Id, draft.InvoiceId);
        Assert.Equal(NfseStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task Marking_as_paid_keeps_the_nfse_already_linked()
    {
        var invoice = Database.SeedInvoice(client.Id, InvoiceStatus.Sent);
        await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id);

        var result = await CreateInvoiceService().MarkAsPaidAsync(invoice.Id);

        Assert.Contains("already has an NFS-e draft", result.Message);
        using var db = Database.CreateContext();
        Assert.Single(db.Nfses);
    }

    [Fact]
    public async Task Failed_payment_does_not_create_a_draft()
    {
        var invoice = Database.SeedInvoice(client.Id, InvoiceStatus.Cancelled);

        var result = await CreateInvoiceService().MarkAsPaidAsync(invoice.Id);

        Assert.False(result.Success);
        using var db = Database.CreateContext();
        Assert.Empty(db.Nfses);
    }

    [Fact]
    public async Task Invoice_with_nfse_cannot_be_deleted()
    {
        var invoice = Database.SeedInvoice(client.Id);
        await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id);

        var result = await CreateInvoiceService().DeleteAsync(invoice.Id);

        Assert.False(result.Success);
        Assert.Contains("Delete the NFS-e first", result.Message);
        Assert.NotNull(Database.GetInvoice(invoice.Id));
    }

    [Fact]
    public async Task Invoice_list_includes_linked_nfse()
    {
        var invoice = Database.SeedInvoice(client.Id);
        await CreateNfseService().CreateDraftForInvoiceAsync(invoice.Id);

        var list = await CreateInvoiceService().GetInvoicesAsync(new());

        Assert.Single(Assert.Single(list).Nfses);
    }
}
