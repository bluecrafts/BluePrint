// The translation layers of a form as the Translation sources panel sees them.
// The report gets the same files through Translations.AddJson / Translations.Add;
// the panel reads them again here, because the report does not tell which layer a
// text came from.

using System.IO;
using System.Text.Json;
using BluePrint.Render.Localization;
using LocalizationShowcase.Forms;

namespace LocalizationShowcase.Translations;

/// <summary>One layer of a form, read for the Translation sources panel.</summary>
/// <param name="Name">What the panel shows: the file name, or "Translator CSV: {file}".</param>
/// <param name="Path">The file, for a double click; null when the layer has no file.</param>
/// <param name="Source">The texts; null when the file could not be read.</param>
/// <param name="Languages">The languages the layer has texts for.</param>
/// <param name="Error">Why the file could not be read.</param>
public sealed record SourceLayer(string Name, string? Path, IReportStringSource? Source,
                                 IReadOnlyList<string> Languages, string? Error)
{
    /// <summary>Reads the file of <paramref name="layer"/>, relative to the folder of the form.</summary>
    public static SourceLayer Read(TranslationLayer layer, string folder)
    {
        var path = System.IO.Path.Combine(folder, layer.Path);
        var file = System.IO.Path.GetFileName(layer.Path);
        var name = layer.Kind == "csv" ? $"Translator CSV: {file}" : file;
        if (!File.Exists(path)) return new SourceLayer(name, path, null, [], "File not found.");
        try
        {
            if (layer.Kind == "csv")
            {
                var csv = new CsvStringSource(path);
                // The Designer writes the source language first: it is there for the translator to read.
                return new SourceLayer(name, path, csv, [.. csv.Languages.Skip(1)], null);
            }
            var json = JsonStringFile.Read(path);
            return new SourceLayer(name, path, json, [json.Language], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException)
        {
            return new SourceLayer(name, path, null, [], ex.Message);
        }
    }
}

/// <summary>
/// One translation file in the format of Translations.AddJson:
/// <c>{ "language": "de", "items": { key: text }, "strings": { key: text } }</c>.
/// </summary>
public sealed class JsonStringFile(string language, Dictionary<string, string> texts) : IReportStringSource
{
    public string Language { get; } = language;

    public bool TryGet(string key, string language, out string text)
    {
        text = "";
        return string.Equals(language, Language, StringComparison.OrdinalIgnoreCase) && texts.TryGetValue(key, out text!);
    }

    /// <exception cref="FormatException">The file is not a translation file: no language, or a text that is not a string.</exception>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    public static JsonStringFile Read(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("language", out var language) || language.ValueKind != JsonValueKind.String
            || !BpLocalization.TryNormalize(language.GetString(), out var tag))
            throw new FormatException("The file must have a \"language\", for example \"de\".");

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in new[] { "items", "strings" })
        {
            if (!root.TryGetProperty(section, out var map)) continue;
            if (map.ValueKind != JsonValueKind.Object) throw new FormatException($"\"{section}\" must be an object of key: text.");
            foreach (var p in map.EnumerateObject())
                texts[p.Name] = p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString()!
                    : throw new FormatException($"The translation of \"{p.Name}\" must be a string.");
        }
        return new JsonStringFile(tag, texts);
    }
}
