// The "ReportServer" section of appsettings.json. Every value can be overridden by an
// environment variable as usual (ReportServer__CacheSize=64), which is how the
// container sets them.

namespace ReportServerApi;

public sealed class ReportServerOptions
{
    public const string Section = "ReportServer";

    /// <summary>The Northwind database (SQLite), opened read-only. Relative = next to the executable.</summary>
    public string Database { get; set; } = "northwind.db";

    /// <summary>The folder with the .bpt files. Relative = next to the executable.</summary>
    public string Reports { get; set; } = "Reports";

    /// <summary>
    /// The folder with the fonts the reports are measured and drawn with. The same files
    /// are served to the browser under /api/fonts, so the HTML is shown with them too.
    /// </summary>
    public string Fonts { get; set; } = "fonts";

    /// <summary>How many rendered reports are kept in memory (one per report and set of conditions).</summary>
    public int CacheSize { get; set; } = 32;

    /// <summary>A rendered report nobody asked for in this many minutes is dropped.</summary>
    public int CacheMinutes { get; set; } = 20;

    /// <summary>Renders that run at the same time; the others wait for a free slot.</summary>
    public int MaxConcurrentRenders { get; set; } = 4;

    /// <summary>A render that takes longer than this is cancelled.</summary>
    public int RenderTimeoutSeconds { get; set; } = 120;

    /// <summary>A relative path in the settings is taken from the folder of the executable.</summary>
    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
