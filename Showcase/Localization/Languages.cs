// The languages of this application: the list on the left and the buttons in the
// toolbar. The list belongs to the application, not to a form - a form of your own
// that does not declare a language still renders in it, with the source text.
//
// Names are in English on purpose: the screen of the application is English, and
// the texts in other scripts are only in the preview, where the report puts them.

namespace LocalizationShowcase;

/// <summary>One language of the Language list.</summary>
/// <param name="Tag">The tag the report is rendered with (BluePrintDocument.Language).</param>
/// <param name="Name">English name shown in the list.</param>
public sealed record AppLanguage(string Tag, string Name)
{
    // The name screen readers and UI Automation give the row.
    public override string ToString() => Name;
}

public static class Languages
{
    public const string Default = "en";

    public static IReadOnlyList<AppLanguage> All { get; } =
    [
        new("en", "English"),
        new("de", "German"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("th", "Thai"),
        new("lo", "Lao"),
        new("zh", "Chinese"),
    ];

    public static AppLanguage Find(string? tag) =>
        All.FirstOrDefault(l => string.Equals(l.Tag, tag, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
