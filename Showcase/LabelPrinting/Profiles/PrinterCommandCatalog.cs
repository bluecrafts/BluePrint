using BluePrint.Render.Export.Mono.Printers;

namespace LabelPrinting.Profiles;

/// <summary>
/// The command languages this application offers, and what it shows for each.
/// <para>Every <see cref="PrinterLanguage"/> BluePrint can encode is either here
/// or in <see cref="NotSupported"/> with the reason: when BluePrint adds a
/// language, this catalog has to decide about it.</para>
/// </summary>
public static class PrinterCommandCatalog
{
    public sealed record Entry(PrinterLanguage Language, string DisplayName, string FileExtension);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(PrinterLanguage.Tspl, "TSPL / TSPL2", ".prn"),
        new(PrinterLanguage.Zpl, "ZPL II", ".zpl"),
        new(PrinterLanguage.Epl, "EPL2", ".prn"),
        new(PrinterLanguage.Ezpl, "EZPL (Godex)", ".prn"),
    ];

    /// <summary>Languages BluePrint can encode that this application leaves out on purpose.</summary>
    public static IReadOnlyDictionary<PrinterLanguage, string> NotSupported { get; } = new Dictionary<PrinterLanguage, string>
    {
        // ESC/POS has no command to find the gap between labels: on a roll of
        // die-cut labels every row lands a little further off. It is the language
        // of receipt printers, and this application prints labels.
        [PrinterLanguage.EscPos] = "ESC/POS is for receipt printers. It cannot find the gap between labels.",
    };

    public static bool IsSupported(PrinterLanguage language) => All.Any(e => e.Language == language);

    public static Entry Of(PrinterLanguage language)
        => All.FirstOrDefault(e => e.Language == language)
           ?? throw new ArgumentOutOfRangeException(nameof(language), language,
                  NotSupported.TryGetValue(language, out var why) ? why : "This printer language is not supported.");
}

/// <summary>Names and recommended pacing for each <see cref="TransportKind"/>.</summary>
public static class TransportCatalog
{
    public sealed record Entry(TransportKind Kind, string DisplayName);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(TransportKind.Tcp, "TCP (LAN / Wi-Fi)"),
        new(TransportKind.Usb, "USB direct"),
        new(TransportKind.SpoolerRaw, "Windows spooler (RAW)"),
        new(TransportKind.Serial, "Serial / Bluetooth (COM)"),
    ];

    public static string NameOf(TransportKind kind) => All.First(e => e.Kind == kind).DisplayName;

    /// <summary>
    /// No pacing over TCP and the spooler, which have flow control of their own.
    /// USB is sent in 4 KB pieces only so that Stop can act between them. Serial
    /// ports - and Bluetooth, which appears as one - often have no flow control
    /// at all, so they get small pieces with a pause.
    /// </summary>
    public static PacingSettings RecommendedPacing(TransportKind kind) => kind switch
    {
        TransportKind.Usb => new() { ChunkBytes = 4096 },
        TransportKind.Serial => new() { ChunkBytes = 512, InterChunkDelayMs = 20 },
        _ => new(),
    };
}
