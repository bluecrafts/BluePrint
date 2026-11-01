namespace ReceiptPrinting.Profiles;

/// <summary>
/// Starting points for a new printer profile. Named after the paper and the print
/// head, not after a brand: any ESC/POS receipt printer of that size and
/// resolution can start from them.
/// </summary>
public static class PrinterPresets
{
    public sealed record Preset(string Name, Func<PrinterProfile> Create);

    public static IReadOnlyList<Preset> All { get; } =
    [
        new("ESC/POS receipt printer - 80 mm, 203 dpi", () => Receipt(203, 576, TransportKind.Usb)),
        // Some printers have a 180 dpi head: the same 72 mm print width is 512 dots there.
        new("ESC/POS receipt printer - 80 mm, 180 dpi", () => Receipt(180, 512, TransportKind.Usb)),
        new("ESC/POS receipt printer - 58 mm, 203 dpi", () => Receipt(203, 384, TransportKind.Usb)),
        new("ESC/POS receipt printer - 80 mm, LAN", () =>
        {
            var p = Receipt(203, 576, TransportKind.Tcp);
            p.Transport.Tcp.Host = "192.0.2.20";   // an address for documentation (RFC 5737): change it
            return p;
        }),
        // A portable printer, paired in Windows: it appears as a COM port.
        new("ESC/POS receipt printer - 58 mm, Bluetooth", () => Receipt(203, 384, TransportKind.Serial)),
        new("Blank", () => Receipt(203, 576, TransportKind.Usb)),
    ];

    // Cut once at the end of the job, with the paper fed past the cutter first.
    private static PrinterProfile Receipt(int dpi, int widthDots, TransportKind transport)
    {
        var profile = new PrinterProfile
        {
            Dpi = dpi,
            PrintWidthDots = widthDots,
            Transport = { Kind = transport },
            Pacing = TransportCatalog.RecommendedPacing(transport),
        };
        profile.Commands.EscPos.FeedLinesAfter = 4;
        profile.Commands.EscPos.CutAtEnd = true;
        return profile;
    }
}
