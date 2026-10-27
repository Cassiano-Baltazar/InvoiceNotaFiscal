using System.Globalization;
using System.Net;
using System.Text;
using InvoiceApp.Web.Models;

namespace InvoiceApp.Web.Services;

public static class InvoiceHtmlBuilder
{
    public static string Build(InvoiceReportModel model)
    {
        var company = model.Company;
        var invoice = model.Invoice;
        var sb = new StringBuilder();

        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>Invoice</title>
            <style>
              @page { margin: 10mm; }
              body { font-family: Arial, Helvetica, sans-serif; color: #111; line-height: 1.4; font-size: 12px; margin: 0; padding: 0; }
              h1 { font-size: 22px; margin: 0 0 12px; }
              h2 { font-size: 15px; margin: 16px 0 8px; }
              hr { border: none; border-top: 1px solid #ccc; margin: 12px 0; }
              table { border-collapse: collapse; width: 100%; margin: 6px 0 12px; }
              th, td { border: 1px solid #ccc; padding: 6px 8px; text-align: left; vertical-align: top; }
              .info-grid { width: 100%; border-collapse: separate; border-spacing: 12px 0; }
              .info-grid td { border: 1px solid #ccc; border-radius: 6px; padding: 16px; vertical-align: top; width: 33%; }
              .info-grid table, .info-grid table td, .info-grid table th { border: none; padding: 0; margin: 0; }
              strong { font-weight: 700; }
              em { font-style: italic; }
              p { margin: 6px 0; }
              ul { margin: 6px 0 6px 18px; }
            </style>
            </head>
            <body>
            """);

        sb.Append("<h1>INVOICE</h1>");
        sb.Append("""
            <table class="info-grid"><tr>
            """);

        sb.Append("<td><strong>Invoice Details</strong><br><br>");
        sb.Append($"<strong>Invoice No.:</strong> {E(invoice.InvoiceNumber)}<br>");
        sb.Append($"<strong>Invoice Date:</strong> {E(model.FormattedInvoiceDate)}<br>");
        sb.Append($"<strong>Due Date:</strong> {E(model.FormattedDueDate)}<br>");
        sb.Append($"<strong>Currency:</strong> {E(invoice.Currency)} ({E(invoice.CurrencyLabel)})<br>");
        sb.Append($"<strong>Payment Terms:</strong> Net {company.PaymentTermsDays} days</td>");

        sb.Append("<td><strong>Bill From</strong><br><br>");
        sb.Append($"<strong>{E(company.LegalName)}</strong><br>");
        sb.Append($"<em>Trade name: {E(company.TradeName)}</em><br><br>");
        sb.Append($"{E(company.AddressLine1)}<br>");
        if (!string.IsNullOrWhiteSpace(company.AddressLine2))
            sb.Append($"{E(company.AddressLine2)}<br>");
        sb.Append($"{E(company.City)}, {E(company.State)}<br>");
        sb.Append($"{E(company.PostalCode)}<br>");
        sb.Append($"{E(company.Country)}<br><br>");
        sb.Append($"<strong>CNPJ:</strong> {E(company.Cnpj)}<br>");
        sb.Append($"<strong>Phone:</strong> {E(company.Phone)}<br>");
        sb.Append($"<strong>Email:</strong> {E(company.Email)}</td>");

        sb.Append("<td><strong>Bill To</strong><br><br>");
        sb.Append($"<strong>{E(invoice.ClientName)}</strong><br><br>");
        AppendAddress(sb, invoice.ClientAddressLine1, invoice.ClientAddressLine2, invoice.ClientAddressLine3,
            invoice.ClientCity, invoice.ClientRegion, invoice.ClientPostalCode, invoice.ClientCountry);
        sb.Append("</td></tr></table>");

        sb.Append("<hr><h2>Services</h2>");
        sb.Append("<table><thead><tr><th>#</th><th>Description</th><th>Qty</th>");
        sb.Append($"<th>Unit Price ({E(invoice.Currency)})</th><th>Amount ({E(invoice.Currency)})</th></tr></thead><tbody>");
        foreach (var item in model.Items)
        {
            sb.Append($"<tr><td>{item.Position}</td>");
            sb.Append($"<td>{E(item.Description)}</td>");
            sb.Append($"<td>{E(InvoiceReportModel.FormatQuantity(item))}</td>");
            sb.Append($"<td>{E(InvoiceReportModel.FormatAmount(item.UnitPrice))}</td>");
            sb.Append($"<td><strong>{E(InvoiceReportModel.FormatAmount(item.Amount))}</strong></td></tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<hr><h2>Summary</h2>");
        sb.Append($"<table><tr><td>Subtotal</td><td>{E(model.FormattedSubtotal)}</td></tr>");
        sb.Append($"<tr><td>Tax</td><td>{E(model.FormattedTax)}</td></tr>");
        sb.Append($"<tr><td><strong>Total Due</strong></td><td><strong>{E(model.FormattedTotal)}</strong></td></tr></table>");

        sb.Append("<hr><h2>Payment Information</h2>");
        sb.Append("<p><strong>Payment method:</strong> Wise</p>");
        sb.Append($"<p><strong>Payment link ({E(invoice.Currency)}):</strong> {E(invoice.WisePaymentLink)}</p>");
        sb.Append("<p>When you open the link above, you will see two payment options:</p>");
        sb.Append("<p><strong>Option 1 — Pay with Wise</strong><br>Log in to your Wise account and complete the payment directly.</p>");
        sb.Append("<p><strong>Option 2 — Bank transfer</strong><br>");
        sb.Append("Select <em>Bank transfer</em>. The page will then display the bank account details to complete the transfer from your bank.</p>");
        sb.Append("<p><strong>Important (bank transfer only):</strong> When paying by bank transfer, use the payment reference code shown on the Wise page ");
        sb.Append($"and also include the invoice number <strong>{E(invoice.InvoiceNumber)}</strong> in your bank transfer reference/notes field.</p>");

        var serviceMonthLabel = new DateOnly(invoice.ServiceYear, invoice.ServiceMonth, 1)
            .ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        sb.Append("<hr><h2>Notes</h2><ul>");
        sb.Append("<li>Services rendered remotely from Brazil.</li>");
        sb.Append($"<li>This invoice is issued for professional software development services performed in {E(serviceMonthLabel)}.</li>");
        sb.Append($"<li>Payment is due within {company.PaymentTermsDays} days of the invoice date.</li></ul>");

        sb.Append("<hr><p><strong>Thank you for your business.</strong></p>");
        sb.Append($"<p>{E(company.LegalName)}</p>");
        sb.Append("</body></html>");

        return sb.ToString();
    }

    private static void AppendAddress(StringBuilder sb, string? line1, string? line2, string? line3,
        string? city, string? region, string? postalCode, string country)
    {
        if (!string.IsNullOrWhiteSpace(line1)) sb.Append($"{E(line1)}<br>");
        if (!string.IsNullOrWhiteSpace(line2)) sb.Append($"{E(line2)}<br>");
        if (!string.IsNullOrWhiteSpace(line3)) sb.Append($"{E(line3)}<br>");
        if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(region))
            sb.Append($"{E(city)}, {E(region)}<br>");
        else if (!string.IsNullOrWhiteSpace(city))
            sb.Append($"{E(city)}<br>");
        else if (!string.IsNullOrWhiteSpace(region))
            sb.Append($"{E(region)}<br>");
        if (!string.IsNullOrWhiteSpace(postalCode)) sb.Append($"{E(postalCode)}<br>");
        sb.Append($"{E(country)}");
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
