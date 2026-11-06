using BluePrint.Render.Export.Mono.Printers;

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
        // A 9-pin dot matrix printer on 76 mm paper (Epson TM-U220): no GS v 0, so the
        // image goes as ESC * columns. Single density is faster; double is sharper.
        new("ESC/POS dot matrix - 76 mm, serial, fast (ESC *)", () => DotMatrix(80, 200, EscPosColumnDensity.Single8)),
        new("ESC/POS dot matrix - 76 mm, serial, fine (ESC *)", () => DotMatrix(160, 400, EscPosColumnDensity.Double8)),
        new("Blank", () => Receipt(203, 576, TransportKind.Usb)),
    ];

    // 200 dots (400 in double density) across 63.4 mm; 72 dpi down the paper. The
    // 72 mm forms are wider than that, so they are shrunk to the head. The cutter is
    // 27 mm past the print line: 7 lines of feed (29.6 mm) before the cut.
    // No pacing: the printer has a 4 KB buffer and RTS/CTS holds the data back when
    // it is full, and pauses only starve a head that prints faster than 9600 baud.
    // Double density prints each pass in one direction: printed both ways, the two
    // passes of a line of text do not line up and the letters lean.
    private static PrinterProfile DotMatrix(int dpi, int widthDots, EscPosColumnDensity density)
    {
        var profile = Receipt(dpi, widthDots, TransportKind.Serial);
        profile.VerticalDpi = 72;
        profile.Raster.ImageWidth = ImageWidthMode.PrintHead;
        profile.Transport.Serial.BaudRate = 9600;
        profile.Pacing = new PacingSettings();
        var e = profile.Commands.EscPos;
        e.ImageCommand = EscPosImageCommand.Column;
        e.ColumnDensity = density;
        e.ColumnLineSpacing = 16;   // 8 dots of 1/72 in, in units of 1/144 in
        e.Unidirectional = density == EscPosColumnDensity.Double8;
        e.FeedLinesAfter = 7;
        return profile;
    }

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
