using System.Text.RegularExpressions;
using InvoiceApp.Web.Services;

namespace InvoiceApp.Tests.Services;

public partial class UiTranslationTests
{
    [GeneratedRegex(@"\bT\(""((?:[^""\\]|\\.)*)""")]
    private static partial Regex TextCall();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    private static DirectoryInfo AppSourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "src", "InvoiceApp.Web"));
            if (candidate.Exists)
                return candidate;
        }

        throw new DirectoryNotFoundException("src/InvoiceApp.Web not found above the test output folder.");
    }

    private static List<(string File, string Text)> TextsInSource()
    {
        var source = AppSourceDirectory();
        return source.EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(f => f.Extension is ".razor" or ".cs")
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => TextCall().Matches(File.ReadAllText(f.FullName))
                .Select(m => (Path.GetRelativePath(source.FullName, f.FullName), Regex.Unescape(m.Groups[1].Value))))
            .ToList();
    }

    [Fact]
    public void Every_text_of_the_screens_has_a_portuguese_translation()
    {
        var texts = TextsInSource();
        Assert.True(texts.Count > 300, $"Only {texts.Count} texts found: the source scan is probably broken.");

        var missing = texts.Where(x => !Ui.PortugueseTexts.ContainsKey(x.Text))
            .Select(x => $"{x.File}: {x.Text}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "Missing Portuguese text:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Translations_keep_the_placeholders()
    {
        var wrong = Ui.PortugueseTexts
            .Where(x => !Placeholder().Matches(x.Key).Select(m => m.Value).Order()
                .SequenceEqual(Placeholder().Matches(x.Value).Select(m => m.Value).Order()))
            .Select(x => x.Key)
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void Translate_returns_portuguese_only_for_portuguese()
    {
        Assert.Equal("Clientes", Ui.Translate("Clients", Ui.Portuguese));
        Assert.Equal("Clients", Ui.Translate("Clients", Ui.English));
        Assert.Equal("Some text with no translation", Ui.Translate("Some text with no translation", Ui.Portuguese));
    }

    [Theory]
    [InlineData("pt", "pt")]
    [InlineData(" PT ", "pt")]
    [InlineData("en", "en")]
    [InlineData("fr", "en")]
    [InlineData(null, "en")]
    public void Unknown_languages_fall_back_to_english(string? code, string expected)
    {
        Assert.Equal(expected, Ui.Normalize(code));
    }
}
