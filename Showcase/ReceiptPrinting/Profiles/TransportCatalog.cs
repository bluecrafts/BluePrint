namespace ReceiptPrinting.Profiles;

/// <summary>Names and recommended pacing for each <see cref="TransportKind"/>.</summary>
public static class TransportCatalog
{
    public sealed record Entry(TransportKind Kind, string DisplayName);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(TransportKind.Usb, "USB direct"),
        new(TransportKind.Tcp, "TCP (LAN / Wi-Fi)"),
        new(TransportKind.SpoolerRaw, "Windows spooler (RAW)"),
        new(TransportKind.Serial, "Serial / Bluetooth (COM)"),
    ];

    public static string NameOf(TransportKind kind) => All.First(e => e.Kind == kind).DisplayName;

    /// <summary>"USB", "TCP", "Spooler", "Serial": the short name of the Print panel's summary.</summary>
    public static string ShortNameOf(TransportKind kind) => kind switch
    {
        TransportKind.Usb => "USB",
        TransportKind.Tcp => "TCP",
        TransportKind.SpoolerRaw => "Spooler",
        _ => "Serial",
    };

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

    /// <summary>
    /// Where the profile sends the data, for a tooltip: "Receipt printer (USB001)",
    /// "192.0.2.20:9100", "COM5", "queue POS-80". Empty while it is not chosen yet.
    /// </summary>
    public static string Destination(TransportSettings t) => t.Kind switch
    {
        TransportKind.Tcp => t.Tcp.Host.Length > 0 ? $"{t.Tcp.Host}:{t.Tcp.Port}" : "",
        TransportKind.Usb => t.Usb.DeviceName.Length > 0 ? t.Usb.DeviceName : t.Usb.DeviceInstanceId,
        TransportKind.SpoolerRaw => t.SpoolerRaw.QueueName.Length > 0 ? $"queue {t.SpoolerRaw.QueueName}" : "",
        _ => t.Serial.PortName,
    };
}
