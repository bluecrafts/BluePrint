using BluePrint.Render.Export.Mono;
using ReceiptPrinting.Profiles;

namespace ReceiptPrinting.Printing;

/// <summary>
/// How a printer profile turns a receipt into a 1-bit image. The preview and the
/// print job both use it, so the image on screen is the one that is printed.
/// </summary>
/// <remarks>
/// <para>By default the image is as wide as the form at the printer's resolution:
/// a 72 mm form is 576 dots at 203 dpi, 512 at 180 dpi. A form narrower than the
/// head prints at its left edge (ESC/POS does not centre an image).</para>
/// <para>"Stretch to the print head" sets the width to the head's instead.
/// BluePrint then works out the resolution from that width and scales the whole
/// receipt to fill it: a 58 mm form on an 80 mm printer prints 50% larger.</para>
/// <para>The strip height is left at BluePrint's default: it is fast enough for a
/// receipt of any length, and the image does not depend on it.</para>
/// </remarks>
public static class RasterPlan
{
    public static MonochromeExportOptions Create(PrinterProfile profile) => new()
    {
        WidthDots = profile.Raster.ImageWidth == ImageWidthMode.PrintHead ? profile.PrintWidthDots : null,
        Dpi = profile.Dpi,
        // Dots that are not square (dot matrix): drawn squeezed down the paper, so they print to size.
        VerticalDpi = profile.VerticalDpi,
        Threshold = (byte)Math.Clamp(profile.Raster.Threshold, 0, 255),
        DitherScope = profile.Raster.DitherScope,
        DitherAlgorithm = profile.Raster.DitherAlgorithm,
    };
}
