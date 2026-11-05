// Where does each text of a form come from, in one language? BluePrint answers
// that itself: BluePrintDocument.TraceTexts walks the same layers in the same order
// as the render - the languages of the fallback chain (ja-JP, then ja), and for each
// language the sources the application added, in their order, then the translations
// embedded in the report. This file only turns the answer into the rows of the
// Translation sources panel: which file each source is, what each layer wins.

using BluePrint.Render;
using BluePrint.Render.Localization;
using LocalizationShowcase.Forms;

namespace LocalizationShowcase.Translations;

/// <summary>One row of the panel: a layer of the manifest and the texts it gives in the language shown.</summary>
/// <param name="Wins">Keys this layer gives.</param>
/// <param name="Replaces">Keys this layer gives that a lower layer (or the report) has too.</param>
/// <param name="OnlySourceLanguage">The layer has texts only in the source language: they are never looked up.</param>
public sealed record LayerUse(SourceLayer Layer, IReadOnlyList<string> Wins, IReadOnlyList<string> Replaces, bool OnlySourceLanguage);

/// <summary>Where every text of a form comes from, in one language.</summary>
public sealed class SourceReport
{
    public required string Language { get; init; }
    public required string SourceLanguage { get; init; }

    /// <summary>The language shown is the source language: nothing is looked up.</summary>
    public bool IsSourceLanguage { get; init; }

    /// <summary>The layers of the manifest, in their order (the order they win).</summary>
    public required IReadOnlyList<LayerUse> Layers { get; init; }

    /// <summary>The languages the report itself has translations for (not the source).</summary>
    public required IReadOnlyList<string> ReportLanguages { get; init; }
    public int ReportWins => Texts.Count(t => t.Kind == TextSourceKind.Report);

    /// <summary>Every text of the report, as BluePrint traced it.</summary>
    public required IReadOnlyList<TextSource> Texts { get; init; }

    public int Total => Texts.Count;

    /// <summary>Texts that show the source text because no layer translates them (0 for the source language).</summary>
    public int Missing => IsSourceLanguage ? 0 : Texts.Count(t => t.Kind == TextSourceKind.SourceText);
}

public static class SourceInspector
{
    /// <summary>
    /// Where every text of <paramref name="document"/> comes from in <paramref name="language"/>.
    /// <paramref name="layers"/> and <paramref name="results"/> are the layers of the manifest in their
    /// order: a layer that could not be added is not one of the document's sources, so the sources the
    /// document counts are the layers that were added, in the same order.
    /// </summary>
    public static SourceReport Inspect(BluePrintDocument document, IReadOnlyList<SourceLayer> layers,
                                       IReadOnlyList<LayerResult> results, string language)
    {
        var trace = document.TraceTexts(language);
        var loc = document.Definition.Localization;
        var source = BpLocalization.TryNormalize(loc?.SourceLanguage, out var s) ? s : BpLocalization.DefaultSourceLanguage;

        // The document's source k = the k-th layer that was added.
        var added = Enumerable.Range(0, results.Count).Where(i => results[i].Error is null).ToList();

        var uses = new List<LayerUse>();
        for (int i = 0; i < layers.Count; i++)
        {
            int index = added.IndexOf(i);
            var won = index < 0 ? [] : trace.Where(t => t.Kind == TextSourceKind.Translations && t.TranslationIndex == index).ToList();
            bool onlySource = layers[i].Languages.Count > 0 && layers[i].Languages.All(l => Same(l, source));
            uses.Add(new LayerUse(layers[i], [.. won.Select(t => t.Key)], [.. won.Where(t => t.Replaces).Select(t => t.Key)], onlySource));
        }

        var reportLanguages = (loc?.Items?.Values ?? Enumerable.Empty<Dictionary<string, string>>())
            .Concat(loc?.Strings?.Values ?? Enumerable.Empty<Dictionary<string, string>>())
            .SelectMany(byLang => byLang.Keys).Where(l => !Same(l, source))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

        return new SourceReport
        {
            Language = language,
            SourceLanguage = source,
            // the report looks nothing up only when the language asked for is the source itself
            IsSourceLanguage = BpLocalization.TryNormalize(language, out var tag) && Same(tag, source),
            Layers = uses,
            ReportLanguages = reportLanguages,
            Texts = trace,
        };
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
