using System.Globalization;
using System.Text;
using InvoiceApp.Web.Data;
using InvoiceApp.Web.Data.Entities;
using InvoiceApp.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Web.Services;

public interface INfseService
{
    Task<List<Nfse>> GetAllAsync(NfseStatus? status = null, CancellationToken cancellationToken = default);
    Task<Nfse?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<List<Nfse>> GetForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);
    Task<List<InvoiceOption>> GetInvoiceOptionsAsync(CancellationToken cancellationToken = default);
    Task<NfseActionResult> CreateDraftForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);
    Task<NfseActionResult> UpdateDraftAsync(int id, NfseEditModel model, CancellationToken cancellationToken = default);
    Task<NfseActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<NfseImportPreview> PreviewImportAsync(string fileName, byte[] content, CancellationToken cancellationToken = default);
    Task<NfseActionResult> ImportAsync(string fileName, byte[] content, int invoiceId, CancellationToken cancellationToken = default);
}

public class NfseService(AppDbContext db) : INfseService
{
    // Values used in the notes already issued (software development for foreign clients).
    // A new draft copies them from the latest note when there is one.
    internal const string DefaultActivityCode = "620230000";
    internal const string DefaultServiceListItem = "10101";
    internal const string DefaultNbsCode = "115022000";
    internal const string DefaultTaxSituation = "14";
    internal const decimal DefaultIssRate = 2.01m;

    public Task<List<Nfse>> GetAllAsync(NfseStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = db.Nfses.Include(x => x.Invoice).AsQueryable();
        if (status is NfseStatus s)
            query = query.Where(x => x.Status == s);

        return query
            .OrderBy(x => x.Status == NfseStatus.Draft ? 0 : 1)
            .ThenByDescending(x => x.IssuedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Nfse?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        db.Nfses.Include(x => x.Invoice).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<List<Nfse>> GetForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default) =>
        db.Nfses
            .Where(x => x.InvoiceId == invoiceId)
            .OrderBy(x => x.Status)
            .ThenByDescending(x => x.IssuedAt)
            .ToListAsync(cancellationToken);

    public async Task<List<InvoiceOption>> GetInvoiceOptionsAsync(CancellationToken cancellationToken = default)
    {
        var invoices = await db.Invoices
            .Where(x => x.Status != InvoiceStatus.Cancelled)
            .OrderByDescending(x => x.IssueYear)
            .ThenByDescending(x => x.IssueMonth)
            .ThenBy(x => x.InvoiceNumber)
            .ToListAsync(cancellationToken);

        return invoices
            .Select(x => new InvoiceOption(x.Id, x.InvoiceNumber, x.ClientName, x.ServiceYear, x.ServiceMonth, x.Total, x.Currency, x.Status))
            .ToList();
    }

    public async Task<NfseActionResult> CreateDraftForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == invoiceId, cancellationToken);
        if (invoice is null)
            return NfseActionResult.Fail("Invoice not found.");
        if (invoice.Status == InvoiceStatus.Cancelled)
            return NfseActionResult.Fail("A cancelled invoice cannot have an NFS-e.");

        var current = await db.Nfses
            .Where(x => x.InvoiceId == invoiceId && (x.Status == NfseStatus.Draft || x.Status == NfseStatus.Issued))
            .FirstOrDefaultAsync(cancellationToken);
        if (current is not null)
        {
            var label = current.Status == NfseStatus.Draft ? "an NFS-e draft" : $"NFS-e nº {current.Number}";
            return NfseActionResult.Ok($"Invoice {invoice.InvoiceNumber} already has {label}.", current.Id);
        }

        var template = await db.Nfses
                .Include(x => x.Invoice)
                .Where(x => x.Status != NfseStatus.Draft && x.Invoice.ClientId == invoice.ClientId)
                .OrderByDescending(x => x.IssuedAt)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await db.Nfses
                .Include(x => x.Invoice)
                .Where(x => x.Status != NfseStatus.Draft)
                .OrderByDescending(x => x.IssuedAt)
                .FirstOrDefaultAsync(cancellationToken);

        var client = await db.Clients.FindAsync([invoice.ClientId], cancellationToken);
        var sameClientTemplate = template?.Invoice.ClientId == invoice.ClientId ? template : null;
        var countryCode = Clean(client?.CountryCode)?.ToUpperInvariant() ?? sameClientTemplate?.TakerCountryCode;

        var draft = new Nfse
        {
            InvoiceId = invoice.Id,
            Status = NfseStatus.Draft,
            Source = NfseSource.Manual,
            TakerName = template?.Invoice.ClientId == invoice.ClientId ? template.TakerName : invoice.ClientName.ToUpperInvariant(),
            TakerCountry = template?.Invoice.ClientId == invoice.ClientId ? template.TakerCountry : invoice.ClientCountry,
            TakerCountryCode = countryCode,
            TakerTaxId = Clean(client?.TaxId) ?? sameClientTemplate?.TakerTaxId,
            TakerMissingTaxIdReason = string.IsNullOrWhiteSpace(client?.TaxId) ? client?.MissingTaxIdReason ?? sameClientTemplate?.TakerMissingTaxIdReason : null,
            CurrencyCode = invoice.Currency.Trim().ToUpperInvariant(),
            AmountForeign = invoice.Total,
            ServiceMode = sameClientTemplate?.ServiceMode ?? ForeignServiceMode.CrossBorder,
            RelatedToTaker = sameClientTemplate?.RelatedToTaker ?? false,
            ResultCountryCode = countryCode,
            Description = BuildDescription(invoice),
            Notes = $"Invoice {invoice.InvoiceNumber} - {invoice.Currency} {invoice.Total.ToString("N2", CultureInfo.InvariantCulture)}",
            ActivityCode = template?.ActivityCode ?? DefaultActivityCode,
            ServiceListItem = template?.ServiceListItem ?? DefaultServiceListItem,
            NbsCode = template?.NbsCode ?? DefaultNbsCode,
            TaxSituation = template?.TaxSituation ?? DefaultTaxSituation,
            IssRate = template?.IssRate ?? DefaultIssRate
        };

        db.Nfses.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        return NfseActionResult.Ok($"NFS-e draft created for invoice {invoice.InvoiceNumber}.", draft.Id);
    }

    public async Task<NfseActionResult> UpdateDraftAsync(int id, NfseEditModel model, CancellationToken cancellationToken = default)
    {
        var nfse = await db.Nfses.FindAsync([id], cancellationToken);
        if (nfse is null)
            return NfseActionResult.Fail("NFS-e not found.");
        if (nfse.Status != NfseStatus.Draft)
            return NfseActionResult.Fail("Only drafts can be edited. Imported NFS-e keep the data from the XML.", id);

        if (string.IsNullOrWhiteSpace(model.TakerName))
            return NfseActionResult.Fail("Taker name is required.", id);
        if (string.IsNullOrWhiteSpace(model.Description))
            return NfseActionResult.Fail("Service description is required.", id);
        if (model.AmountBrl < 0)
            return NfseActionResult.Fail("Amount in BRL cannot be negative.", id);
        if (model.IssRate < 0 || model.IssRate > 100)
            return NfseActionResult.Fail("ISS rate must be between 0 and 100.", id);
        if (new[] { model.IrAmount, model.InssAmount, model.CsllAmount, model.PisAmount, model.CofinsAmount, model.IssWithheldAmount }.Any(x => x < 0))
            return NfseActionResult.Fail("Tax amounts cannot be negative.", id);
        if (model.AmountForeign < 0)
            return NfseActionResult.Fail("Amount in the foreign currency cannot be negative.", id);
        if (!IsCode(model.TakerCountryCode, 2) || !IsCode(model.ResultCountryCode, 2))
            return NfseActionResult.Fail("Country codes must have 2 letters (ISO), e.g. GB, CA, US.", id);
        if (!IsCode(model.CurrencyCode, 3))
            return NfseActionResult.Fail("Currency must have 3 letters (ISO), e.g. GBP, CAD, USD.", id);

        nfse.TakerName = model.TakerName.Trim();
        nfse.TakerCountry = Clean(model.TakerCountry);
        nfse.TakerCountryCode = Clean(model.TakerCountryCode)?.ToUpperInvariant();
        nfse.ServiceDate = model.ServiceDate;
        nfse.Description = model.Description.Trim();
        nfse.Notes = Clean(model.Notes);
        nfse.ActivityCode = Clean(model.ActivityCode);
        nfse.ServiceListItem = Clean(model.ServiceListItem);
        nfse.NbsCode = Clean(model.NbsCode);
        nfse.TaxSituation = Clean(model.TaxSituation);
        nfse.IssRate = model.IssRate;
        nfse.AmountBrl = Math.Round(model.AmountBrl, 2);
        nfse.IrAmount = model.IrAmount;
        nfse.InssAmount = model.InssAmount;
        nfse.CsllAmount = model.CsllAmount;
        nfse.PisAmount = model.PisAmount;
        nfse.CofinsAmount = model.CofinsAmount;
        nfse.IssWithheldAmount = model.IssWithheldAmount;
        nfse.TakerTaxId = Clean(model.TakerTaxId);
        nfse.TakerMissingTaxIdReason = nfse.TakerTaxId is null ? model.TakerMissingTaxIdReason : null;
        nfse.CurrencyCode = Clean(model.CurrencyCode)?.ToUpperInvariant();
        nfse.AmountForeign = Math.Round(model.AmountForeign, 2);
        nfse.ServiceMode = model.ServiceMode;
        nfse.RelatedToTaker = model.RelatedToTaker;
        nfse.ResultCountryCode = Clean(model.ResultCountryCode)?.ToUpperInvariant();
        nfse.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return NfseActionResult.Ok("NFS-e draft saved.", id);
    }

    public async Task<NfseActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var nfse = await db.Nfses.FindAsync([id], cancellationToken);
        if (nfse is null)
            return NfseActionResult.Fail("NFS-e not found.");

        db.Nfses.Remove(nfse);
        await db.SaveChangesAsync(cancellationToken);
        var label = nfse.Status == NfseStatus.Draft ? "NFS-e draft" : $"NFS-e nº {nfse.Number}";
        return NfseActionResult.Ok($"{label} deleted.");
    }

    public async Task<NfseImportPreview> PreviewImportAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        NfseXmlData data;
        try
        {
            data = NfseXmlParser.Parse(content);
        }
        catch (FormatException ex)
        {
            return new NfseImportPreview { FileName = fileName, Content = content, Error = ex.Message };
        }

        return new NfseImportPreview
        {
            FileName = fileName,
            Content = content,
            Data = data,
            AlreadyImported = await FindDuplicateAsync(data, cancellationToken),
            SuggestedInvoiceId = await SuggestInvoiceAsync(data, cancellationToken)
        };
    }

    public async Task<NfseActionResult> ImportAsync(string fileName, byte[] content, int invoiceId, CancellationToken cancellationToken = default)
    {
        NfseXmlData data;
        try
        {
            data = NfseXmlParser.Parse(content);
        }
        catch (FormatException ex)
        {
            return NfseActionResult.Fail($"{fileName}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(data.Number))
            return NfseActionResult.Fail($"{fileName}: the XML has no NFS-e number (it was not issued yet).");

        var duplicate = await FindDuplicateAsync(data, cancellationToken);
        if (duplicate is not null)
            return NfseActionResult.Fail(
                $"{fileName}: NFS-e nº {data.Number} was already imported (invoice {duplicate.Invoice.InvoiceNumber}).", duplicate.Id);

        var invoice = await db.Invoices.FindAsync([invoiceId], cancellationToken);
        if (invoice is null)
            return NfseActionResult.Fail($"{fileName}: invoice not found.");
        if (invoice.Status == InvoiceStatus.Cancelled)
            return NfseActionResult.Fail($"{fileName}: invoice {invoice.InvoiceNumber} is cancelled.");

        var linked = await db.Nfses.Where(x => x.InvoiceId == invoiceId).ToListAsync(cancellationToken);
        if (data.Status == NfseStatus.Issued)
        {
            var issued = linked.FirstOrDefault(x => x.Status == NfseStatus.Issued);
            if (issued is not null)
                return NfseActionResult.Fail(
                    $"{fileName}: invoice {invoice.InvoiceNumber} already has the issued NFS-e nº {issued.Number}. " +
                    "If it was replaced, import its XML again with the new status or delete it first.");
        }

        // An issued note takes the place of the draft prepared for the same invoice.
        var nfse = data.Status == NfseStatus.Issued ? linked.FirstOrDefault(x => x.Status == NfseStatus.Draft) : null;
        var replacedDraft = nfse is not null;
        if (nfse is null)
        {
            nfse = new Nfse
            {
                InvoiceId = invoiceId,
                CurrencyCode = invoice.Currency.Trim().ToUpperInvariant(),
                AmountForeign = invoice.Total,
                ResultCountryCode = data.TakerCountryCode
            };
            db.Nfses.Add(nfse);
        }

        Apply(nfse, data, fileName);
        await db.SaveChangesAsync(cancellationToken);

        var warnings = new List<string>();
        var company = await db.CompanySettings.FirstOrDefaultAsync(cancellationToken);
        var companyCnpj = Digits(company?.Cnpj);
        if (companyCnpj.Length >= 8 && companyCnpj[..8].Trim('0').Length > 0 && Digits(data.ProviderCnpj) is var xmlCnpj && xmlCnpj.Length > 0 && xmlCnpj != companyCnpj)
            warnings.Add($"{fileName}: the provider CNPJ in the XML ({data.ProviderCnpj}) is not the company CNPJ in Settings.");

        var message = $"NFS-e nº {data.Number} imported and linked to invoice {invoice.InvoiceNumber}" +
                      (replacedDraft ? " (it replaced the draft)." : ".");
        return NfseActionResult.Ok(message, nfse.Id, warnings);
    }

    private async Task<Nfse?> FindDuplicateAsync(NfseXmlData data, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(data.NationalAccessKey))
        {
            var byKey = await db.Nfses.Include(x => x.Invoice)
                .FirstOrDefaultAsync(x => x.NationalAccessKey == data.NationalAccessKey, cancellationToken);
            if (byKey is not null)
                return byKey;
        }

        if (string.IsNullOrWhiteSpace(data.Number))
            return null;

        return await db.Nfses.Include(x => x.Invoice)
            .FirstOrDefaultAsync(x => x.Number == data.Number && x.Series == data.Series, cancellationToken);
    }

    /// <summary>
    /// Suggests the invoice an XML belongs to: same client (by name) and, when the text mentions it,
    /// the same service month. The user always confirms the choice.
    /// </summary>
    internal async Task<int?> SuggestInvoiceAsync(NfseXmlData data, CancellationToken cancellationToken)
    {
        var takerKey = NameKey(data.TakerName);
        if (takerKey.Length == 0)
            return null;

        var invoices = await db.Invoices
            .Include(x => x.Nfses)
            .Where(x => x.Status != InvoiceStatus.Cancelled)
            .ToListAsync(cancellationToken);

        // Full name match first; the first word alone only when no client matches the whole name.
        var sameClient = invoices
            .Where(x => NameKey(x.ClientName) is var key && key.Length > 0 && (key.StartsWith(takerKey) || takerKey.StartsWith(key)))
            .ToList();
        if (sameClient.Count == 0)
            sameClient = invoices
                .Where(x => NameKey(x.ClientName) is var key && key.Length > 0 && FirstWord(key) == FirstWord(takerKey))
                .ToList();
        if (sameClient.Count == 0)
            return null;

        var months = FindMonths(data.Description).Concat(FindMonths(data.Notes)).ToList();

        var best = sameClient
            .Select(invoice => new
            {
                Invoice = invoice,
                Score = (months.Count > 0 && months[0] == (invoice.ServiceYear, invoice.ServiceMonth) ? 4 : 0)
                        + (months.Contains((invoice.ServiceYear, invoice.ServiceMonth)) ? 2 : 0)
                        + (invoice.Nfses.All(n => n.Status != NfseStatus.Issued) ? 1 : 0)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Invoice.IssueYear * 100 + x.Invoice.IssueMonth)
            .First();

        // Without a month match the guess is weak: only suggest when there is a single open candidate.
        if (best.Score < 2 && sameClient.Count(x => x.Nfses.All(n => n.Status != NfseStatus.Issued)) != 1)
            return null;

        return best.Invoice.Id;
    }

    private static readonly string[] EnglishMonths =
        ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];

    private static readonly string[] PortugueseMonths =
        ["janeiro", "fevereiro", "marco", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    public static IEnumerable<(int Year, int Month)> FindMonths(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var words = RemoveAccents(text.ToLowerInvariant())
            .Split([' ', ',', '-', '/', '.', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < words.Length; i++)
        {
            var month = Array.IndexOf(EnglishMonths, words[i]);
            if (month < 0)
                month = Array.IndexOf(PortugueseMonths, words[i]);
            if (month < 0)
                continue;

            // "September 2026" or "setembro de 2026"
            for (var j = i + 1; j < Math.Min(words.Length, i + 3); j++)
            {
                if (words[j].Length == 4 && int.TryParse(words[j], out var year) && year > 2000)
                {
                    yield return (year, month + 1);
                    break;
                }
            }
        }
    }

    private static void Apply(Nfse nfse, NfseXmlData data, string fileName)
    {
        nfse.Status = data.Status;
        nfse.Source = NfseSource.Imported;
        nfse.Number = data.Number;
        nfse.Series = data.Series;
        nfse.IssuedAt = data.IssuedAt;
        nfse.ServiceDate = data.ServiceDate;
        nfse.VerificationCode = data.VerificationCode;
        nfse.NationalAccessKey = data.NationalAccessKey;
        nfse.Link = data.Link;
        nfse.MunicipalStatusCode = data.MunicipalStatusCode;
        nfse.MunicipalStatusDescription = data.MunicipalStatusDescription;
        nfse.ProviderCnpj = data.ProviderCnpj;
        nfse.TakerName = data.TakerName;
        nfse.TakerCountry = data.TakerCountry;
        nfse.TakerCountryCode = data.TakerCountryCode;
        nfse.Description = data.Description;
        nfse.Notes = data.Notes;
        nfse.ActivityCode = data.ActivityCode;
        nfse.ServiceListItem = data.ServiceListItem;
        nfse.NbsCode = data.NbsCode;
        nfse.TaxSituation = data.TaxSituation;
        nfse.IssRate = data.IssRate;
        nfse.AmountBrl = data.AmountBrl;
        nfse.Discount = data.Discount;
        nfse.IrAmount = data.IrAmount;
        nfse.InssAmount = data.InssAmount;
        nfse.CsllAmount = data.CsllAmount;
        nfse.PisAmount = data.PisAmount;
        nfse.CofinsAmount = data.CofinsAmount;
        nfse.IssWithheldAmount = data.IssWithheldAmount;
        nfse.FileName = fileName;
        nfse.XmlContent = data.XmlContent;
        nfse.UpdatedAt = DateTime.UtcNow;
    }

    private static string BuildDescription(Invoice invoice)
    {
        var items = invoice.Items.OrderBy(x => x.Position).Select(x => x.Description).ToList();
        var text = items.Count > 0 ? string.Join("; ", items) : invoice.ServiceDescription;
        return text.Replace(" — ", " - ").Replace('—', '-');
    }

    private static bool IsCode(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) || (value.Trim().Length == length && value.Trim().All(char.IsAsciiLetter));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string NameKey(string? name) =>
        new(RemoveAccents((name ?? string.Empty).ToLowerInvariant()).Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

    private static string FirstWord(string key) => key.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private static string RemoveAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
