// Localization.cfg: what the window remembers between runs, in one JSON file next
// to the executable - the form, document and language chosen last, the theme, the
// size and position of the window and the width of the left panel.
//
// Nothing in it is precious: a file that cannot be read gives the defaults. It is
// written to a .tmp file first and then moved over the old one, so a crash halfway
// through leaves the old file whole.

using System.IO;
using System.Text.Json;

namespace LocalizationShowcase;

public sealed class AppConfig
{
    /// <summary>
    /// The form chosen last. Relative to the application's folder when it is inside
    /// it (the samples), so the folder can be moved as a whole.
    /// </summary>
    public string? LastForm { get; set; }

    /// <summary>Name of the document chosen last ("10248", "Orders 10248-10257 (10 invoices)").</summary>
    public string? LastDocument { get; set; }

    /// <summary>Tag of the language chosen last.</summary>
    public string LastLanguage { get; set; } = Languages.Default;

    /// <summary>"Light", "Dark" or "System" (follow the Windows app mode). An unknown value means System.</summary>
    public string Theme { get; set; } = "System";

    public WindowPlacement? Window { get; set; }

    public double LeftPanelWidth { get; set; } = 300;
    public bool LeftPanelVisible { get; set; } = true;
    public double RightPanelWidth { get; set; } = 300;
    public bool RightPanelVisible { get; set; } = true;

    /// <summary>The Translation sources panel is open.</summary>
    public bool SourcesExpanded { get; set; } = true;
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public sealed class AppConfigStore(string folder)
{
    public const string FileName = "Localization.cfg";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>The store next to the executable.</summary>
    public static AppConfigStore Default { get; } = new(AppContext.BaseDirectory);

    public string Folder { get; } = folder;
    public string FilePath => Path.Combine(Folder, FileName);

    /// <summary>Reads the file. A missing file, or one that is not valid, gives the defaults.</summary>
    public AppConfig Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? new AppConfig()
                : new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }

    /// <exception cref="IOException">The folder cannot be written to.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be written to.</exception>
    public void Save(AppConfig config)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Json));
        File.Move(temp, FilePath, overwrite: true);
    }
}
