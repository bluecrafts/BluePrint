// The fonts of a language are Windows fonts (Yu Gothic UI for Japanese, Malgun Gothic
// for Korean, Microsoft YaHei UI for Chinese). A computer without one of them - a
// Windows edition without the language's fonts, or fonts removed to save space -
// prints that language in a fallback font, and the text may come out as boxes. Say
// so above the preview instead of letting the invoice look broken.

using System.Windows;
using System.Windows.Media;
using BluePrint.Core.Models;
using BluePrint.Render.Localization;
using BluePrint.UI.Controls;

namespace LocalizationShowcase;

public partial class MainWindow
{
    // The font families of this computer, read once.
    private static readonly Lazy<HashSet<string>> InstalledFonts = new(() =>
        new HashSet<string>(Fonts.SystemFontFamilies.SelectMany(f => f.FamilyNames.Values.Append(f.Source)),
                            StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The fonts the form prints with in <paramref name="language"/> - each element's font after the
    /// font rules of the language (localization.fonts) - that are not installed.
    /// </summary>
    public static IReadOnlyList<string> MissingFonts(ReportDefinition form, string language, ISet<string> installed)
    {
        var loc = form.Localization;
        return [.. form.Sections.AllElements()
            .Where(e => e.Type is ElementType.Label or ElementType.Field or ElementType.Computed)
            .Select(e => e.KeepFont ? e.Style.FontFamily : BpLocalization.FontFor(loc, e.Style.FontFamily, language, out _) ?? e.Style.FontFamily)
            .Where(f => !string.IsNullOrWhiteSpace(f) && !installed.Contains(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    private void ShowFontWarning()
    {
        var missing = _openForm is null ? [] : MissingFonts(_openForm.Document.Definition, CurrentLanguage.Tag, InstalledFonts.Value);
        FontWarning.SetLines(BpMessageKind.Warning,
            missing.Select(f => $"Font {f} is not installed. {CurrentLanguage.Name} text may not display."));
        FontWarning.Visibility = missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
