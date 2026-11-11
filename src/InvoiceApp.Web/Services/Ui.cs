using System.Globalization;

namespace InvoiceApp.Web.Services;

public sealed record UiLanguageOption(string Code, string Short, string Name, string HtmlLang);

/// <summary>
/// Language of the app screens. The texts are written in English in the code and <see cref="T(string)"/>
/// looks up the translation of the language chosen in Settings (or in the sidebar), falling back to English.
/// The app runs on the user's machine for one person, so the choice is global: it is loaded from the database
/// at startup and changed by the /language endpoint. Documents (invoice PDF, NFS-e XML and DANFSe) are not translated.
/// </summary>
public static partial class Ui
{
    public const string English = "en";
    public const string Portuguese = "pt";

    public static IReadOnlyList<UiLanguageOption> Languages { get; } =
    [
        new(English, "EN", "English", "en"),
        new(Portuguese, "PT", "Português (Brasil)", "pt-BR")
    ];

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static volatile string language = English;

    public static string Language
    {
        get => language;
        set => language = Normalize(value);
    }

    public static bool IsPortuguese => Language == Portuguese;

    public static string HtmlLang => Languages.First(x => x.Code == Language).HtmlLang;

    /// <summary>The known language code for <paramref name="code"/>, or English when it is not one.</summary>
    public static string Normalize(string? code)
    {
        var normalized = (code ?? string.Empty).Trim().ToLowerInvariant();
        return Languages.Any(x => x.Code == normalized) ? normalized : English;
    }

    public static bool IsSupported(string? code) =>
        Languages.Any(x => x.Code == (code ?? string.Empty).Trim().ToLowerInvariant());

    /// <summary>The text in the current language.</summary>
    public static string T(string text) => Translate(text, Language);

    /// <summary>The text in the current language, with {0}, {1}... replaced by <paramref name="args"/>.</summary>
    public static string T(string text, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(text), args);

    public static string Translate(string text, string language) =>
        language == Portuguese && PortugueseTexts.TryGetValue(text, out var translated) ? translated : text;

    /// <summary>"August 2026" or "agosto de 2026".</summary>
    public static string MonthYear(DateOnly date) =>
        IsPortuguese
            ? date.ToString("MMMM 'de' yyyy", PtBr)
            : date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}
