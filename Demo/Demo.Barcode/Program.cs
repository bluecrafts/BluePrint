// Every symbology BluePrint.Barcode encodes, written out as SVG and PNG.
//
//   dotnet run --project Demo.Barcode
//
// The library splits the work in two, and this sample walks both halves:
//
//   BluePrint.Barcode         encodes. It turns a string into a grid of
//                             modules and nothing else - no pixels, no DPI, no
//                             graphics library. It can also write that grid
//                             straight out as SVG.
//   BluePrint.Barcode.Render  draws. It takes the same grid and paints it with
//                             SkiaSharp, adding the human readable line under
//                             the bars.
//
// Encoding on its own has no graphics dependency at all, which is the point of
// the split: a service that only needs an SVG never has to load Skia.

using System.Globalization;
using BluePrint.Barcode;
using BluePrint.Barcode.Layout;
using BluePrint.Barcode.Output;
using BluePrint.Barcode.Render;
using Demo.Barcode;
using Demo.Shared;
using SkiaSharp;

var outputDir = Path.Combine(AppContext.BaseDirectory, "barcodes");
Directory.CreateDirectory(outputDir);

// The library never picks a font for you. Whoever draws owns the typeface and
// keeps it alive, so the same symbol can be printed in the host's own face.
using var hriTypeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
const float HriSizePx = 15f;

var sheet = new List<SheetEntry>();

Console.WriteLine();
Console.WriteLine("  symbology        kind    modules    size (px)    human readable");
Console.WriteLine("  ---------------- ------- ---------- ------------ ------------------------------");

foreach (var sample in BarcodeSamples.All)
{
    // ---- 1. encode ---------------------------------------------------------
    //
    // A result, never an exception and never a silent null. Everything that can
    // be wrong with a value - a bad check digit, a character the symbology
    // cannot carry, more data than it holds - comes back as a status you can
    // show, instead of a symbol nobody can scan.
    var result = BarcodeEncoders.Default.Encode(sample.Symbology, sample.Data, sample.EncodeOptions);
    if (!result.IsOk)
    {
        Console.WriteLine($"  {sample.Symbology,-16} FAILED  {result.Status}: {result.Message}");
        continue;
    }

    var symbol = result.Symbol!;
    bool is2d = symbol.Kind == SymbolKind.Matrix2D;

    // The symbol knows how wide it is in modules, including the quiet zone that
    // has to stay clear around it. Work the pixel size out from that and the
    // symbol always fits exactly, whatever it turned out to contain.
    int totalCols = symbol.Columns + symbol.QuietZoneLeft + symbol.QuietZoneRight;
    int totalRows = symbol.Rows + 2 * symbol.QuietZoneY;

    int moduleSize = is2d ? Math.Clamp(420 / totalCols, 3, 8) : 3;
    int barWidth = totalCols * moduleSize;
    int barHeight = is2d ? totalRows * moduleSize : 90;

    // ---- 2. SVG, from BluePrint.Barcode alone ------------------------------
    //
    // Snap is None here on purpose. Snapping module edges to whole units is for
    // a raster, where a fractional edge blurs and the contrast a scanner needs
    // is lost. An SVG is rasterised later at a resolution nobody here knows, so
    // the honest thing is to leave the geometry exact.
    var layout = BarcodeLayoutBuilder.Fit(symbol, barWidth, barHeight,
        new BarcodeLayoutOptions
        {
            Snap = BarcodeSnapMode.None,
            FixedModuleSize = moduleSize,
        });

    if (layout is null)
    {
        Console.WriteLine($"  {sample.Symbology,-16} FAILED  no room for the symbol at {barWidth}x{barHeight}px");
        continue;
    }

    string svg = BarcodeSvgWriter.Write(layout, new BarcodeSvgOptions { Background = "#ffffff" });
    File.WriteAllText(Path.Combine(outputDir, sample.FileStem + ".svg"), svg);

    // ---- 3. PNG, through BluePrint.Barcode.Render --------------------------
    //
    // Same symbol, drawn as pixels. ShowHri puts the readable line underneath;
    // for EAN and UPC that line is not one string but groups of digits sitting
    // under their own half of the symbol, with the guard bars reaching down
    // past them. The renderer follows whatever the symbology requires, and
    // whether the line is wanted at all is the caller's decision - see
    // BarcodeSample.ShowsHri.
    bool showHri = sample.ShowsHri(symbol);
    int pngHeight = barHeight + (showHri ? (int)(HriSizePx * 1.6) : 0);

    using var render = SkiaBarcodeRenderer.Render(new BarcodeRenderRequest
    {
        Symbol = symbol,
        PixelWidth = barWidth,
        PixelHeight = pngHeight,
        FixedModuleSize = moduleSize,
        ShowHri = showHri,
        HriTypeface = hriTypeface,
        HriSizePx = HriSizePx,
        Background = SKColors.White,
        Foreground = SKColors.Black,
    });

    if (!render.IsOk)
    {
        Console.WriteLine($"  {sample.Symbology,-16} FAILED  {render.Status}: {render.Message}");
        continue;
    }

    File.WriteAllBytes(Path.Combine(outputDir, sample.FileStem + ".png"), BarcodePng.Encode(render.Bitmap!));

    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "  {0,-16} {1,-7} {2,-10} {3,-12} {4}",
        sample.Symbology,
        is2d ? "2D" : "linear",
        $"{symbol.Rows}x{symbol.Columns}",
        $"{barWidth}x{pngHeight}",
        symbol.HriText ?? "(none)"));

    sheet.Add(new SheetEntry(sample, symbol, svg, showHri));
}

// ---- 4. one page showing all of them ---------------------------------------
var sheetPath = Path.Combine(outputDir, "index.html");
File.WriteAllText(sheetPath, ContactSheet.Build(sheet));

Console.WriteLine();
Console.WriteLine($"  Wrote {sheet.Count} symbols to {outputDir}");
Console.WriteLine($"  Open {sheetPath} to see them all on one page.");

// ---- 5. what a value the symbology cannot carry looks like ------------------
//
// Worth running once. A report that prints [QRCode: 12345] where the symbol
// should be tells the person reading it nothing, so the library answers with
// the reason instead and leaves the decision to the caller.
Console.WriteLine();
Console.WriteLine("  Values that cannot be encoded, and what comes back:");
Console.WriteLine();

Explain(BarcodeSymbology.EAN13, "5901234123450", "a thirteenth digit that is not the right check digit");
Explain(BarcodeSymbology.ITF, "12345", "an odd number of digits, and ITF encodes them in pairs");
Explain(BarcodeSymbology.Codabar, "A12X45B", "X is not one of the twenty characters Codabar carries");
Explain(BarcodeSymbology.QRCode, new string('x', 3000), "more than a QR Code holds, even at version 40",
    new BarcodeEncodeOptions { ErrorCorrection = BarcodeEcLevel.H });

// The drawing side answers the same way. Nothing is ever squeezed into a box
// too small for it: below a readable module size a symbol stops being a symbol.
using (var tooSmall = SkiaBarcodeRenderer.Render(new BarcodeRenderRequest
{
    Symbol = BarcodeEncoders.Default.Encode(BarcodeSymbology.QRCode, "too small").Symbol!,
    PixelWidth = 16,
    PixelHeight = 16,
}))
{
    Console.WriteLine($"    {"QRCode in 16x16px",-26} {tooSmall.Status,-22} {tooSmall.Message}");
}

Console.WriteLine();

static void Explain(BarcodeSymbology symbology, string data, string why, BarcodeEncodeOptions? options = null)
{
    var result = BarcodeEncoders.Default.Encode(symbology, data, options);
    string shown = data.Length <= 20 ? data : data[..17] + "...";
    string label = symbology + " " + shown;

    Console.WriteLine($"    {label,-26} {result.Status,-22} {result.Message}");
    Console.WriteLine($"    {"",-26} {"",-22} ({why})");
}
