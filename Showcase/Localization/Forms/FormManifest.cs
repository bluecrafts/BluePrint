// The translation layers of a form: <form>.layers.json next to the .bpt file says
// which translation files the application adds, in which order, and how the form
// is listed. The application never guesses the files from the name of a form.
//
//   { "group": "In separate files",
//     "layers": [ { "kind": "json", "path": "Invoice-Mixed.th.json" }, ... ],
//     "description": "..." }
//
// The first layer added wins. The translations inside the .bpt file are always
// under every layer, so they are not listed.

using System.IO;
using System.Text.Json;
using BluePrint.Render;
using LocalizationShowcase.Translations;

namespace LocalizationShowcase.Forms;

/// <summary>One translation file of a form.</summary>
/// <param name="Kind"><c>json</c> (one language, read with Translations.AddJson) or <c>csv</c> (every language).</param>
/// <param name="Path">Relative to the folder of the .bpt file.</param>
public sealed record TranslationLayer(string Kind, string Path);

/// <summary>What happened to one layer when a form was loaded.</summary>
/// <param name="Layer">The layer of the manifest.</param>
/// <param name="Error">Why it could not be added; null when it was added.</param>
public sealed record LayerResult(TranslationLayer Layer, string? Error);

/// <summary>The <c>&lt;form&gt;.layers.json</c> of a form.</summary>
public sealed class FormManifest
{
    public const string InTheReport = "In the report";
    public const string InSeparateFiles = "In separate files";
    public const string SharedGlossary = "Shared glossary";
    public const string HostProvider = "Host provider";

    /// <summary>The groups of the Forms list, in the order they are shown.</summary>
    public static IReadOnlyList<string> Groups { get; } = [InTheReport, InSeparateFiles, SharedGlossary, HostProvider];

    public string Group { get; init; } = InTheReport;
    public string? Description { get; init; }
    public IReadOnlyList<TranslationLayer> Layers { get; init; } = [];

    /// <summary>The manifest file of the form at <paramref name="bptPath"/>.</summary>
    public static string PathFor(string bptPath) => System.IO.Path.ChangeExtension(bptPath, ".layers.json");

    /// <summary>
    /// The manifest of the form at <paramref name="bptPath"/>, or the default (no layers, "In the report")
    /// when the form has none.
    /// </summary>
    /// <exception cref="FormatException">The manifest is not valid: bad JSON, an unknown group or kind, or a path outside the folder.</exception>
    public static FormManifest Read(string bptPath)
    {
        var file = PathFor(bptPath);
        if (!File.Exists(file)) return new FormManifest();

        var name = System.IO.Path.GetFileName(file);
        JsonDocument json;
        try { json = JsonDocument.Parse(File.ReadAllText(file)); }
        catch (JsonException ex) { throw new FormatException($"{name}: not valid JSON. {ex.Message}", ex); }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException($"{name}: must be a JSON object.");

            var group = String(root, "group") ?? InTheReport;
            if (!Groups.Contains(group))
                throw new FormatException($"{name}: unknown group \"{group}\". Use one of: {string.Join(", ", Groups)}.");

            var layers = new List<TranslationLayer>();
            if (root.TryGetProperty("layers", out var list))
            {
                if (list.ValueKind != JsonValueKind.Array) throw new FormatException($"{name}: \"layers\" must be a list.");
                foreach (var item in list.EnumerateArray())
                {
                    var kind = String(item, "kind");
                    var path = String(item, "path");
                    if (kind is not ("json" or "csv"))
                        throw new FormatException($"{name}: a layer has kind \"{kind}\". Use json or csv.");
                    if (string.IsNullOrWhiteSpace(path) || System.IO.Path.IsPathRooted(path)
                        || path.Replace('\\', '/').Split('/').Contains(".."))
                        throw new FormatException($"{name}: the layer path \"{path}\" must be a file in the folder of the form.");
                    layers.Add(new TranslationLayer(kind, path));
                }
            }
            return new FormManifest { Group = group, Description = String(root, "description"), Layers = layers };
        }
    }

    /// <summary>
    /// Adds every layer to <paramref name="document"/> in the order of the manifest. A layer that cannot be
    /// read is skipped and reported, so the form still opens with the layers that work.
    /// </summary>
    public IReadOnlyList<LayerResult> ApplyTo(BluePrintDocument document, string bptPath)
    {
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(bptPath))!;
        var results = new List<LayerResult>();
        foreach (var layer in Layers)
        {
            var file = System.IO.Path.Combine(folder, layer.Path);
            try
            {
                if (layer.Kind == "json") document.Translations.AddJson(File.ReadAllText(file));
                else document.Translations.Add(new CsvStringSource(file));
                results.Add(new LayerResult(layer, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
            {
                results.Add(new LayerResult(layer, ex.Message));
            }
        }
        return results;
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
