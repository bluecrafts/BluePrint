using BluePrint.Render.Export.Mono;
using LabelPrinting.Profiles;

namespace LabelPrinting.Printing;

/// <summary>
/// How a printer profile turns rows of labels into 1-bit images: drawn at the
/// printer's resolution, as wide as the row of labels and no wider.
/// </summary>
/// <remarks>
/// The width is never set to the print head's. BluePrint would then work out the
/// resolution from that width and scale the whole label up to fill it - a 100 mm
/// row on a 104 mm head would print 4% too large, taller than the label too.
/// The printer prints a narrower image from the left edge of the head.
/// </remarks>
public static class RasterPlan
{
    public static MonochromeExportOptions Create(PrinterProfile profile) => new()
    {
        WidthDots = null,
        Dpi = profile.Dpi,
        Threshold = (byte)Math.Clamp(profile.Raster.Threshold, 0, 255),
        DitherScope = profile.Raster.DitherScope,
        DitherAlgorithm = profile.Raster.DitherAlgorithm,
        Orientation = profile.Raster.Orientation,
        Mirror = profile.Raster.Mirror,
        Negative = profile.Raster.Negative,
    };
}
