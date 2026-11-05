// Opening a form: the .bpt file, then its translation layers in the order of its
// manifest. One form is one BluePrintDocument - a new form never reuses the
// document of the one before, so its translation layers cannot leak into the next.

using System.IO;
using BluePrint.Render;
using LocalizationShowcase.Translations;

namespace LocalizationShowcase.Forms;

/// <summary>A form opened for the preview: the document and what happened to each of its layers.</summary>
/// <param name="Entry">The form of the list.</param>
/// <param name="Document">Ready to retrieve; the language is set by the caller.</param>
/// <param name="Layers">Every layer of the manifest, with the reason when it could not be added.</param>
/// <param name="Sources">The same layers, read again for the Translation sources panel (same order).</param>
public sealed record OpenedForm(FormEntry Entry, BluePrintDocument Document, IReadOnlyList<LayerResult> Layers,
                                IReadOnlyList<SourceLayer> Sources)
{
    public IEnumerable<LayerResult> BrokenLayers => Layers.Where(l => l.Error is not null);

    /// <summary>Where every text comes from in <paramref name="language"/>.</summary>
    public SourceReport Inspect(string language) => SourceInspector.Inspect(Document, Sources, Layers, language);
}

public static class FormLoader
{
    /// <summary>
    /// Loads the report and adds its layers. A layer that cannot be read is skipped and reported
    /// in <see cref="OpenedForm.Layers"/>: the form still opens with the layers that work.
    /// </summary>
    /// <exception cref="Exception">The .bpt file itself cannot be read: the message says why.</exception>
    public static OpenedForm Open(FormEntry entry)
    {
        var document = BluePrintDocument.Load(entry.Path);
        var layers = entry.Manifest.ApplyTo(document, entry.Path);
        var folder = Path.GetDirectoryName(Path.GetFullPath(entry.Path))!;
        var sources = entry.Manifest.Layers.Select(l => SourceLayer.Read(l, folder)).ToList();
        return new OpenedForm(entry, document, layers, sources);
    }
}
