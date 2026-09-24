// One set of sample values, shared by both barcode samples.
//
// Demo.Barcode links this file, and so does Demo.Barcode.Wpf, the same way all
// three report samples share nw-05-invoice.bpt. Keeping the list in one place
// means the console sheet and the on-screen gallery always show the same
// symbols, and each entry can say in one line what makes that symbology
// different from the one above it.
//
// Every value here is valid input. Where a symbology ends in a check digit the
// sample leaves it off on purpose: the encoder works it out and puts it in the
// text it hands back for printing under the bars.

using BluePrint.Barcode;

namespace Demo.Shared;

/// <param name="Title">What to show above the symbol.</param>
/// <param name="Symbology">Which encoder runs.</param>
/// <param name="Data">Exactly what is handed to Encode.</param>
/// <param name="Family">Groups the list on screen and in the contact sheet.</param>
/// <param name="Note">Why this symbology exists, in one line.</param>
/// <param name="Options">Null means BarcodeEncodeOptions.Default.</param>
public sealed record BarcodeSample(
    string Title,
    BarcodeSymbology Symbology,
    string Data,
    string Family,
    string Note,
    BarcodeEncodeOptions? Options = null)
{
    /// <summary>Options to hand to the encoder, never null.</summary>
    public BarcodeEncodeOptions EncodeOptions => Options ?? BarcodeEncodeOptions.Default;

    /// <summary>File-name stem for the written PNG and SVG.</summary>
    public string FileStem => Symbology.ToString();

    /// <summary>
    /// Whether the readable text belongs under this symbol.
    /// </summary>
    /// <remarks>
    /// The encoder fills HriText in whenever there is something worth printing,
    /// but where it goes is the host's decision. It belongs under a linear
    /// symbol, which a person may have to key in by hand, and under GS1
    /// DataMatrix, where the standard asks for the bracketed form to be
    /// readable. Under a QR code it would only be a long line nobody reads.
    /// </remarks>
    public bool ShowsHri(BarcodeSymbol symbol) =>
        !string.IsNullOrEmpty(symbol.HriText) &&
        (symbol.Kind == SymbolKind.Linear1D || Symbology == BarcodeSymbology.GS1DataMatrix);
}

public static class BarcodeSamples
{
    public const string Linear = "Linear";
    public const string Retail = "Retail";
    public const string Matrix = "2D";
    public const string Gs1 = "GS1";

    /// <summary>Every symbology the library encodes, in the order they are shown.</summary>
    public static IReadOnlyList<BarcodeSample> All { get; } =
    [
        // ---- Linear ---------------------------------------------------------
        new("Code 39", BarcodeSymbology.Code39, "BLUEPRINT-39", Linear,
            "Digits, capitals and a few punctuation marks. Self-checking, so the check character is optional."),

        new("Code 128", BarcodeSymbology.Code128, "BluePrint 128", Linear,
            "The whole ASCII range, packed densely. The usual choice when the content is not a retail number."),

        new("ITF", BarcodeSymbology.ITF, "1234567895", Linear,
            "Digits only, in pairs, printed on corrugated board where bars spread as the ink soaks in."),

        new("Codabar", BarcodeSymbology.Codabar, "A20260920B", Linear,
            "Digits and six symbols, framed by a start and stop letter A-D. Still standard on blood bags and film."),

        // ---- Retail ---------------------------------------------------------
        new("EAN-13", BarcodeSymbology.EAN13, "590123412345", Retail,
            "The barcode on a retail pack. Twelve digits in, the thirteenth is the check digit the encoder adds."),

        new("EAN-8", BarcodeSymbology.EAN8, "9638507", Retail,
            "EAN-13 shortened for packs too small to carry it, such as a tube of lip balm."),

        new("UPC-A", BarcodeSymbology.UPCA, "01234567890", Retail,
            "The North American form of EAN-13: the same bars, with a leading zero that is never printed."),

        new("UPC-E", BarcodeSymbology.UPCE, "012345", Retail,
            "UPC-A with runs of zeros squeezed out, for a pack with almost no flat surface."),

        // ---- GS1 ------------------------------------------------------------
        new("GS1-128", BarcodeSymbology.GS1_128, "(01)09521234543213(10)LOT123", Gs1,
            "Code 128 carrying labelled fields. The brackets mark where each field starts and are not encoded."),

        new("GS1 DataMatrix", BarcodeSymbology.GS1DataMatrix, "(01)09521234543213(17)261231(10)LOT123", Gs1,
            "The same labelled fields in a square, which is what fits on a vial or a surgical instrument."),

        // ---- 2D -------------------------------------------------------------
        new("QR Code", BarcodeSymbology.QRCode, "https://github.com/bluecrafts/BluePrint", Matrix,
            "Reads from any angle and survives damage. Raise the correction level when a logo sits on top.",
            new BarcodeEncodeOptions { ErrorCorrection = BarcodeEcLevel.Q }),

        new("DataMatrix", BarcodeSymbology.DataMatrix, "BluePrint DataMatrix", Matrix,
            "The smallest 2D symbol for a short string. Marked straight onto metal parts."),

        new("PDF417", BarcodeSymbology.PDF417, "BluePrint PDF417 carries a paragraph, not a number.", Matrix,
            "A stack of linear rows. Holds far more text than a 1D symbol, which is why it is on driving licences.",
            new BarcodeEncodeOptions { Pdf417Columns = 6 }),

        new("Aztec", BarcodeSymbology.Aztec, "BluePrint Aztec", Matrix,
            "No quiet zone required, so it fits a crowded ticket. Finder rings sit in the middle, not the corners."),
    ];
}
