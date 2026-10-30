// LabelPrinting.cfg: the printer profiles and the application's own settings,
// in one JSON file next to the executable.
//
// Losing the printer profiles is the one thing this file must never do, so:
//   - it is written to a .tmp file first and then moved over the old one, so a
//     crash halfway through leaves the old file whole;
//   - a file that is not JSON at all is renamed to .bad-<time> before anything
//     else happens, so the next save cannot overwrite it;
//   - a single profile that cannot be read (a value edited by hand into
//     something unknown) is left out of the list, but its JSON is kept as it
//     was and written back on every save, so it is still there to be fixed.

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LabelPrinting.Profiles;

public sealed class AppConfigStore
{
    public const string FileName = "LabelPrinting.cfg";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // The file is meant to be read and fixed by hand: write "&" in a USB
        // device id as it is, not as a Unicode escape. It is never put in HTML.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // Profiles that could not be read, as they were in the file.
    private readonly List<JsonNode> _unreadProfiles = [];

    public AppConfigStore(string folder) => Folder = folder;

    /// <summary>The store next to the executable.</summary>
    public static AppConfigStore Default { get; } = new(AppContext.BaseDirectory);

    public string Folder { get; }
    public string FilePath => Path.Combine(Folder, FileName);

    /// <summary>What happened while reading, for the application to tell the user.</summary>
    public sealed record LoadReport(string? RenamedTo, IReadOnlyList<string> SkippedProfiles);

    /// <summary>Reads the file. A missing file gives the defaults.</summary>
    public (AppConfig Config, LoadReport Report) Load()
    {
        _unreadProfiles.Clear();
        var config = new AppConfig();
        var skipped = new List<string>();

        if (!File.Exists(FilePath)) return (config, new(null, skipped));

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath));
        }
        catch (JsonException)
        {
            var renamed = $"{FilePath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, renamed);
            return (config, new(renamed, skipped));
        }

        // The application's settings: anything wrong in them just means defaults.
        if (root?["app"] is { } app)
        {
            try { config.App = app.Deserialize<AppSettings>(Json) ?? new(); }
            catch (JsonException) { config.App = new(); }
        }

        // The profiles one by one, so that one bad entry does not take the rest.
        if (root?["printerProfiles"] is JsonArray profiles)
        {
            foreach (var node in profiles)
            {
                if (node is null) continue;
                var name = (node as JsonObject)?["name"]?.GetValueKind() == JsonValueKind.String
                    ? node["name"]!.GetValue<string>()
                    : "(no name)";
                try
                {
                    var profile = node.Deserialize<PrinterProfile>(Json)
                                  ?? throw new JsonException("The entry is empty.");
                    if (string.IsNullOrWhiteSpace(profile.Name))
                        throw new JsonException("The profile has no name.");
                    if (config.PrinterProfiles.Any(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new JsonException("Another profile already has this name.");
                    // A language BluePrint knows but this application leaves out (ESC/POS):
                    // kept in the file like any profile that cannot be used.
                    if (PrinterCommandCatalog.NotSupported.TryGetValue(profile.PrinterCommand, out var why))
                        throw new JsonException(why);
                    config.PrinterProfiles.Add(profile);
                }
                catch (JsonException ex)
                {
                    _unreadProfiles.Add(node.DeepClone());
                    skipped.Add($"{name}: {ex.Message}");
                }
            }
        }

        return (config, new(null, skipped));
    }

    /// <summary>
    /// Writes <paramref name="config"/>, with the profiles that could not be
    /// read when the file was loaded kept after it, unchanged.
    /// </summary>
    /// <exception cref="IOException">The folder cannot be written to.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be written to.</exception>
    public void Save(AppConfig config)
    {
        var root = new JsonObject
        {
            ["app"] = JsonSerializer.SerializeToNode(config.App, Json),
        };
        var profiles = new JsonArray();
        foreach (var profile in config.PrinterProfiles)
            profiles.Add(JsonSerializer.SerializeToNode(profile, Json));
        foreach (var unread in _unreadProfiles)
            profiles.Add(unread.DeepClone());
        root["printerProfiles"] = profiles;

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(Json));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>A deep copy of a profile, for editing without touching the original.</summary>
    public static PrinterProfile Clone(PrinterProfile profile)
        => JsonSerializer.Deserialize<PrinterProfile>(JsonSerializer.Serialize(profile, Json), Json)!;
}
