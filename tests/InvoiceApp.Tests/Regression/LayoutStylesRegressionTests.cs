using System.Net;
using System.Text.RegularExpressions;
using InvoiceApp.Tests.Api;

namespace InvoiceApp.Tests.Regression;

/// <summary>
/// Regression: App.razor linked "InvoiceApp.Web.styles.css", but the assembly is named "InvoiceApp",
/// so the real bundle is "InvoiceApp.styles.css". The link returned 404, the scoped CSS of
/// MainLayout never loaded and the "An unhandled error has occurred" bar was always visible.
/// </summary>
public partial class LayoutStylesRegressionTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [GeneratedRegex("<link[^>]+rel=\"stylesheet\"[^>]+href=\"([^\"]+)\"")]
    private static partial Regex StylesheetLink();

    private async Task<List<string>> GetStylesheetsAsync()
    {
        var html = await client.GetStringAsync("/");
        return StylesheetLink().Matches(html).Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public async Task Every_stylesheet_linked_by_the_page_exists()
    {
        var stylesheets = await GetStylesheetsAsync();

        Assert.NotEmpty(stylesheets);
        foreach (var href in stylesheets)
        {
            var response = await client.GetAsync("/" + href.TrimStart('/'));
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{href} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Scoped_css_bundle_is_linked_and_hides_the_error_bar()
    {
        var bundle = (await GetStylesheetsAsync()).SingleOrDefault(h => h.Contains(".styles.css"));
        Assert.NotNull(bundle);

        var css = await client.GetStringAsync("/" + bundle.TrimStart('/'));

        // The rule that keeps the error bar hidden until a real error happens.
        Assert.Matches(@"#blazor-error-ui\[[^\]]+\]\s*\{[^}]*display:\s*none", css);
    }
}
