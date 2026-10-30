using BluePrint.Render.Export.Mono.Printers;

namespace LabelPrinting.Profiles;

/// <summary>
/// Checks printer profiles before they are saved. The ranges of the command
/// options are the ones BluePrint's encoder accepts; a value outside them would
/// only fail later, at print time.
/// </summary>
public static class ProfileValidator
{
    public sealed record Problem(string Field, string Message)
    {
        public override string ToString() => $"{Field}: {Message}";
    }

    /// <summary>Problems of one profile, against the others in the same list.</summary>
    public static IEnumerable<Problem> Validate(PrinterProfile p, IEnumerable<PrinterProfile> all)
    {
        if (string.IsNullOrWhiteSpace(p.Name))
            yield return new("Name", "Enter a name.");
        else if (all.Any(o => !ReferenceEquals(o, p) && string.Equals(o.Name.Trim(), p.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            yield return new("Name", "A profile with this name already exists.");

        if (p.Dpi is < 100 or > 1200)
            yield return new("Resolution", "Must be between 100 and 1200 dpi.");
        if (p.PrintWidthDots <= 0)
            yield return new("Print width", "Must be more than 0.");
        else if (p.PrintWidthDots % 8 != 0)
            yield return new("Print width", "Must be a multiple of 8 dots.");

        if (!PrinterCommandCatalog.IsSupported(p.PrinterCommand))
            yield return new("Printer command", "Choose a label printer language.");

        if (p.Label.WidthMm <= 0) yield return new("Row width", "Must be greater than 0.");
        if (p.Label.HeightMm <= 0) yield return new("Row height", "Must be greater than 0.");

        if (p.Raster.Threshold is < 0 or > 255)
            yield return new("Threshold", "Must be between 0 and 255.");

        foreach (var problem in ValidateTransport(p.Transport)) yield return problem;

        if (p.Pacing.ChunkBytes is < 0 or > 65536)
            yield return new("Chunk size", "Must be between 0 and 65536.");
        if (p.Pacing.InterChunkDelayMs is < 0 or > 10000)
            yield return new("Delay between chunks", "Must be between 0 and 10000 ms.");
        if (p.Pacing.InterPageDelayMs is < 0 or > 10000)
            yield return new("Delay between rows", "Must be between 0 and 10000 ms.");

        foreach (var problem in ValidateCommands(p.Commands)) yield return problem;
    }

    // Only the settings of the kind in use are checked: the others are kept for
    // switching back, and may well be empty. Which printer to reach (host,
    // device, queue, port) may stay empty too - a profile made from a preset is
    // saved before the printer is chosen - and is asked for when printing.
    private static IEnumerable<Problem> ValidateTransport(TransportSettings t)
    {
        switch (t.Kind)
        {
            case TransportKind.Tcp:
                if (t.Tcp.Port is < 1 or > 65535) yield return new("Port", "Must be between 1 and 65535.");
                if (t.Tcp.ConnectTimeoutMs is < 100 or > 60000) yield return new("Timeout", "Must be between 100 and 60000 ms.");
                break;
            case TransportKind.Serial:
                if (t.Serial.BaudRate <= 0) yield return new("Baud rate", "Must be greater than 0.");
                if (t.Serial.DataBits is < 5 or > 8) yield return new("Data bits", "Must be between 5 and 8.");
                break;
        }
    }

    private static IEnumerable<Problem> ValidateCommands(PrinterCommandOptions c)
    {
        if (c.Copies is < 1 or > 999) yield return new("Copies", "Must be between 1 and 999.");

        if (c.Tspl.GapMm is < 0) yield return new("TSPL gap", "Must be 0 or more.");
        if (c.Tspl.Density is < 0 or > 15) yield return new("TSPL density", "Must be between 0 and 15.");
        if (c.Tspl.Speed is { } speed && (speed <= 0 || speed > 18)) yield return new("TSPL speed", "Must be greater than 0 and at most 18.");

        if (c.Zpl.LabelHomeX < 0 || c.Zpl.LabelHomeY < 0) yield return new("Label home", "Must be 0 or more.");

        if (c.Epl.GapDots is < 0 or > 65535) yield return new("EPL2 gap", "Must be between 0 and 65535 dots.");
        if (c.Epl.Darkness is < 0 or > 15) yield return new("EPL2 darkness", "Must be between 0 and 15.");
        if (c.Epl.Speed is < 0 or > 6) yield return new("EPL2 speed", "Must be between 0 and 6.");

        if (c.Ezpl.GapMm is < 0 or > 100) yield return new("EZPL gap", "Must be between 0 and 100 mm.");
        if (c.Ezpl.Darkness is < 0 or > 19) yield return new("EZPL darkness", "Must be between 0 and 19.");
        if (c.Ezpl.Speed is < 1 or > 10) yield return new("EZPL speed", "Must be between 1 and 10.");
    }
}
