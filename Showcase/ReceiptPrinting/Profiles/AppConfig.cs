namespace ReceiptPrinting.Profiles;

/// <summary>Everything this application keeps between runs: the content of ReceiptPrinting.cfg.</summary>
public sealed class AppConfig
{
    public AppSettings App { get; set; } = new();
    public List<PrinterProfile> PrinterProfiles { get; set; } = [];
}

public sealed class AppSettings
{
    /// <summary>
    /// The form chosen last. Relative to the application's folder when it is
    /// inside it (the samples), so the folder can be moved as a whole.
    /// </summary>
    public string? LastForm { get; set; }

    /// <summary>Name of the document chosen last ("Order 10248").</summary>
    public string? LastDocument { get; set; }

    /// <summary>Name of the printer profile chosen last.</summary>
    public string? SelectedProfile { get; set; }

    /// <summary>"Light", "Dark" or "System" (follow the Windows app mode). An unknown value means System.</summary>
    public string Theme { get; set; } = "System";

    public WindowPlacement? Window { get; set; }

    public double LeftPanelWidth { get; set; } = 280;
    public double RightPanelWidth { get; set; } = 320;
    public bool LeftPanelVisible { get; set; } = true;
    public bool RightPanelVisible { get; set; } = true;

    /// <summary>The groups of the Printer Profile panel that are open: "general", "connection"...</summary>
    public List<string> Expanded { get; set; } = ["general", "connection"];

    /// <summary>Zoom of the 1-bit image: "0.5x", "1x"... An unknown value means 1x.</summary>
    public string PreviewZoom { get; set; } = "1x";
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}
