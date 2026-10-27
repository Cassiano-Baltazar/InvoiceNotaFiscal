using InvoiceApp.Tests.Infrastructure;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;

namespace InvoiceApp.Tests.Services;

public class InvoiceServiceQueryTests : ServiceTestBase
{
    private readonly Client alpha;
    private readonly Client beta;

    public InvoiceServiceQueryTests()
    {
        Database.SeedCompany();
        alpha = Database.SeedClient("Alpha");
        beta = Database.SeedClient("Beta");
        Database.SeedInvoice(alpha.Id, InvoiceStatus.Paid, issueYear: 2026, issueMonth: 9);
        Database.SeedInvoice(beta.Id, InvoiceStatus.Sent, issueYear: 2026, issueMonth: 9);
        Database.SeedInvoice(alpha.Id, InvoiceStatus.Generated, issueYear: 2026, issueMonth: 10);
        Database.SeedInvoice(beta.Id, InvoiceStatus.Cancelled, issueYear: 2026, issueMonth: 10);
    }

    private Task<List<Invoice>> Query(InvoiceFilterModel filter) => CreateInvoiceService().GetInvoicesAsync(filter);

    [Fact]
    public async Task Returns_all_ordered_by_newest_issue_month_then_number()
    {
        var result = await Query(new InvoiceFilterModel());

        Assert.Equal(
            ["INV-2026-10-001", "INV-2026-10-002", "INV-2026-09-001", "INV-2026-09-002"],
            result.Select(x => x.InvoiceNumber));
        Assert.All(result, x => Assert.NotNull(x.Client));
    }

    [Fact]
    public async Task Filters_by_client() =>
        Assert.All(await Query(new InvoiceFilterModel { ClientId = alpha.Id }), x => Assert.Equal("Alpha", x.ClientName));

    [Fact]
    public async Task Filters_by_issue_year_and_month()
    {
        var result = await Query(new InvoiceFilterModel { IssueYear = 2026, IssueMonth = 9 });
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.Equal(9, x.IssueMonth));
        Assert.Empty(await Query(new InvoiceFilterModel { IssueYear = 2025 }));
    }

    [Fact]
    public async Task Filters_by_service_year_and_month()
    {
        var result = await Query(new InvoiceFilterModel { ServiceYear = 2026, ServiceMonth = 9 });
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.Equal(10, x.IssueMonth));
    }

    [Theory]
    [InlineData(InvoiceStatus.Generated)]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task Filters_by_status(InvoiceStatus status)
    {
        var result = await Query(new InvoiceFilterModel { Status = status });
        Assert.Equal(status, Assert.Single(result).Status);
    }

    [Theory]
    [InlineData("Beta", 2)]
    [InlineData(" 10-001 ", 1)]
    [InlineData("nobody", 0)]
    public async Task Searches_by_number_or_client_name(string search, int expected) =>
        Assert.Equal(expected, (await Query(new InvoiceFilterModel { Search = search })).Count);

    [Fact]
    public async Task Get_invoice_and_report_model()
    {
        var service = CreateInvoiceService();
        var id = (await Query(new InvoiceFilterModel())).First().Id;

        Assert.NotNull(await service.GetInvoiceAsync(id));
        var report = await service.GetReportModelAsync(id);
        Assert.NotNull(report);
        Assert.Equal("TESTCO", report.Company.TradeName);
        Assert.Null(await service.GetInvoiceAsync(999));
        Assert.Null(await service.GetReportModelAsync(999));
    }

    [Fact]
    public async Task Export_pdf_returns_bytes_or_throws_when_missing()
    {
        var service = CreateInvoiceService();
        var id = (await Query(new InvoiceFilterModel())).First().Id;

        Assert.Equal(FakePdfExportService.PdfBytes, await service.ExportPdfAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportPdfAsync(999));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SavePdfToDiskAsync(999));
    }

    [Fact]
    public async Task Save_pdf_to_disk_uses_month_folder_and_sequence()
    {
        var invoice = (await Query(new InvoiceFilterModel { Search = "INV-2026-09-002" })).Single();

        var path = await CreateInvoiceService().SavePdfToDiskAsync(invoice.Id);

        Assert.EndsWith(Path.Combine("2026-09", "PDF", "2026-09-002.pdf"), path);
        Assert.Equal(path, Database.GetInvoice(invoice.Id).PdfPath);
    }
}
