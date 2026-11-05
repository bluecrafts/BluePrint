// The Forms list: every .bpt file in Samples next to the executable, grouped by
// where its translations are kept (the "group" of its .layers.json), plus any form
// opened with File > Open Form. A form is only read - the application has no
// designer and never saves one.

using System.IO;
using BluePrint.Core.Serialization;

namespace LocalizationShowcase.Forms;

/// <summary>One entry of the Forms list.</summary>
public sealed class FormEntry
{
    /// <summary>The group of a form opened with File > Open Form.</summary>
    public const string Other = "Other";

    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>One of <see cref="FormManifest.Groups"/>, or <see cref="Other"/>.</summary>
    public required string Group { get; init; }

    /// <summary>The text under the list: the description of the manifest, or of the report.</summary>
    public string Description { get; init; } = "";

    /// <summary>The translation layers the application adds when the form is opened.</summary>
    public FormManifest Manifest { get; init; } = new();

    /// <summary>Why the manifest could not be read; the form still opens, with its own translations only.</summary>
    public string? ManifestError { get; init; }

    /// <summary>Opened with File > Open Form, not found in Samples.</summary>
    public bool IsExternal { get; init; }

    public bool CanRead { get; init; } = true;

    /// <summary>Order of the groups in the list: the manifest groups in their order, then Other.</summary>
    public int GroupOrder
    {
        get
        {
            int i = FormManifest.Groups.ToList().IndexOf(Group);
            return i < 0 ? FormManifest.Groups.Count : i;
        }
    }

    /// <summary>Has a warning to show next to the name: a manifest that is not valid, or a report that cannot be read.</summary>
    public bool HasProblem => ManifestError is not null || !CanRead;

    // The name screen readers and UI Automation give the list item.
    public override string ToString() => Name;
}

public static class FormCatalog
{
    /// <summary>Every form in <paramref name="folder"/>, read for the list (not opened).</summary>
    public static List<FormEntry> Read(string folder) =>
        Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder, "*.bpt").Order(StringComparer.OrdinalIgnoreCase).Select(p => Entry(p, external: false))]
            : [];

    /// <summary>
    /// One form, read for the list. A form opened with File > Open Form is in <see cref="FormEntry.Other"/>
    /// and its manifest is not read: only the Samples folder is trusted to list files to load.
    /// </summary>
    public static FormEntry Entry(string path, bool external)
    {
        string description;
        try
        {
            description = BptSerializer.Load(path).Metadata.Description;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new FormEntry { Path = path, Group = external ? FormEntry.Other : FormManifest.InTheReport,
                                   Description = $"Could not open {System.IO.Path.GetFileName(path)}: {ex.Message}",
                                   IsExternal = external, CanRead = false };
        }

        if (external)
            return new FormEntry { Path = path, Group = FormEntry.Other, Description = description, IsExternal = true };

        try
        {
            var manifest = FormManifest.Read(path);
            return new FormEntry
            {
                Path = path, Group = manifest.Group, Manifest = manifest,
                Description = string.IsNullOrWhiteSpace(manifest.Description) ? description : manifest.Description,
            };
        }
        catch (FormatException ex)
        {
            return new FormEntry
            {
                Path = path, Group = FormManifest.InTheReport, Description = description,
                ManifestError = $"The layer list of {System.IO.Path.GetFileNameWithoutExtension(path)} is not valid: {ex.Message}",
            };
        }
    }
}
