namespace ReceiptPrinting.Profiles;

/// <summary>
/// Checks a printer profile before it is saved. The ranges of the ESC/POS options
/// are the ones BluePrint's encoder accepts, or narrower where the printer is: a
/// value outside them would only fail later, at print time.
/// </summary>
public static class ProfileValidator
{
    public sealed record Problem(string Field, string Message)
    {
        public override string ToString() => $"{Field}: {Message}";
    }

    /// <summary>The tallest block of one GS v 0 command that ESC/POS printers take.</summary>
    public const int MaxStripHeight = 2303;

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

        if (p.Raster.Threshold is < 0 or > 255)
            yield return new("Threshold", "Must be between 0 and 255.");

        foreach (var problem in ValidateTransport(p.Transport)) yield return problem;

        if (p.Pacing.ChunkBytes is < 0 or > 65536)
            yield return new("Chunk size", "Must be between 0 and 65536.");
        if (p.Pacing.InterChunkDelayMs is < 0 or > 10000)
            yield return new("Delay between chunks", "Must be between 0 and 10000 ms.");
        if (p.Pacing.InterPageDelayMs is < 0 or > 10000)
            yield return new("Delay between pages", "Must be between 0 and 10000 ms.");

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

    private static IEnumerable<Problem> ValidateCommands(ReceiptCommands c)
    {
        if (c.Copies is < 1 or > 99) yield return new("Copies", "Must be between 1 and 99.");

        var e = c.EscPos;
        if (e.FeedLinesAfter is < 0 or > 255) yield return new("Feed after image", "Must be between 0 and 255 lines.");
        if (e.MaxStripHeight is < 1 or > MaxStripHeight) yield return new("Strip height", $"Must be between 1 and {MaxStripHeight} dots.");
        if (e.DrawerOnMs is < 0 or > 510) yield return new("Pulse on", "Must be between 0 and 510 ms.");
        if (e.DrawerOffMs is < 0 or > 510) yield return new("Pulse off", "Must be between 0 and 510 ms.");
    }
}
