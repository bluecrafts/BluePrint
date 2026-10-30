using BluePrint.Render.Export.Mono.Printers;

namespace LabelPrinting.Profiles;

/// <summary>
/// Starting points for a new printer profile. Named after the command language
/// and the print head, not after a brand: any printer that speaks the language
/// at that resolution can start from them.
/// </summary>
public static class PrinterPresets
{
    public sealed record Preset(string Name, Func<PrinterProfile> Create);

    public static IReadOnlyList<Preset> All { get; } =
    [
        new("TSPL label printer - 203 dpi, 4 inch", () => Label(PrinterLanguage.Tspl, 203, 832, TransportKind.Usb)),
        new("ZPL label printer - 203 dpi, 4 inch", () => Label(PrinterLanguage.Zpl, 203, 832, TransportKind.Tcp)),
        new("ZPL label printer - 300 dpi, 4 inch", () => Label(PrinterLanguage.Zpl, 300, 1248, TransportKind.Tcp)),
        new("EPL2 label printer - 203 dpi, 4 inch", () => Label(PrinterLanguage.Epl, 203, 832, TransportKind.Usb)),
        new("EZPL label printer - 203 dpi, 4 inch", () => Label(PrinterLanguage.Ezpl, 203, 832, TransportKind.Usb)),
        new("Blank", () => Label(PrinterLanguage.Tspl, 203, 832, TransportKind.Tcp)),
    ];

    // A 4 inch head, a 100 x 50 mm row of labels - two 50 x 50 mm labels across,
    // the size of the sample label - and a 2 mm gap between rows.
    private static PrinterProfile Label(PrinterLanguage language, int dpi, int widthDots, TransportKind transport)
    {
        var profile = new PrinterProfile
        {
            PrinterCommand = language,
            Dpi = dpi,
            PrintWidthDots = widthDots,
            Label = { WidthMm = 100, HeightMm = 50 },
            Transport = { Kind = transport },
            Pacing = TransportCatalog.RecommendedPacing(transport),
        };
        profile.Commands.Tspl.GapMm = 2;
        return profile;
    }
}
