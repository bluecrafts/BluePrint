namespace LabelPrinting.Profiles;

/// <summary>Everything this application keeps between runs: the content of LabelPrinting.cfg.</summary>
public sealed class AppConfig
{
    public AppSettings App { get; set; } = new();
    public List<PrinterProfile> PrinterProfiles { get; set; } = [];
}

public sealed class AppSettings
{
    /// <summary>
    /// The label opened last. Relative to the application's folder when it is
    /// inside it (the sample), so the folder can be moved as a whole.
    /// </summary>
    public string? LastReport { get; set; }

    /// <summary>Name of the printer profile chosen last.</summary>
    public string? SelectedProfile { get; set; }

    public WindowPlacement? Window { get; set; }

    /// <summary>The panel on the right of each tab (Properties, Print). The names are older than these panels.</summary>
    public bool PrintPanelVisible { get; set; } = true;
    public double PrintPanelWidth { get; set; } = 300;

    /// <summary>"Light", "Dark" or "System" (follow the Windows app mode). An unknown value means System.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>"Design" or "Preview" ("Monochrome" from before the tab was renamed is read as Preview).</summary>
    public string ActiveTab { get; set; } = "Design";

    /// <summary>Zoom of the design surface; 2 = 200%.</summary>
    public double DesignZoom { get; set; } = 2;

    /// <summary>Zoom of the 1-bit image in the Preview tab: "0.5x", "1x"... An unknown value means 1x.</summary>
    public string RowImageZoom { get; set; } = "1x";
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}
