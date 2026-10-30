using System.Globalization;
using BluePrint.Core.Models;
using BluePrint.Render.Export.Mono;
using BluePrint.UI.Controls;
using LabelPrinting.Profiles;

namespace LabelPrinting.Printing;

/// <summary>The size of a 1-bit image that was rendered.</summary>
public readonly record struct ImageSize(int WidthDots, int HeightDots, double Dpi);

/// <summary>
/// What is worth knowing before printing. A report that is not a label, and a
/// landscape row of several labels, cannot be printed; the rest are warnings,
/// and the job can still be sent.
/// </summary>
public static class RasterChecks
{
    /// <summary>A label height in the report this far from the profile's means the wrong roll.</summary>
    public const double LabelHeightToleranceMm = 1.0;

    /// <summary>This application prints label reports only.</summary>
    public static bool IsLabelReport(ReportDefinition report) => report.Report.Type == ReportType.Label;

    /// <param name="image">The first row's image when it is known, for the checks that need its size.</param>
    public static IReadOnlyList<(BpMessageKind Kind, string Text)> Run(
        ReportDefinition report, PrinterProfile profile, ImageSize? image)
    {
        var result = new List<(BpMessageKind, string)>();
        if (!IsLabelReport(report))
        {
            result.Add((BpMessageKind.Error, "This is not a label report. LabelPrinting prints label reports only."));
            return result;
        }

        var rowPerPage = report.Report.LabelLayout?.PageMode == LabelPageMode.RowPerPage;
        var sideways = profile.Raster.Orientation is MonoOrientation.Landscape or MonoOrientation.Landscape180;

        // Turning a row of several labels would line them up along the roll
        // instead of across the head; BluePrint refuses to make the image.
        if (sideways && rowPerPage && report.Report.LabelLayout is { LabelsPerRow: > 1 })
        {
            result.Add((BpMessageKind.Error,
                "Landscape orientation needs one label per row. " +
                "Use one label per row, or set Orientation to Portrait in the printer profile."));
            return result;
        }

        if (image is { } img && img.WidthDots > profile.PrintWidthDots)
            result.Add((BpMessageKind.Warning, !sideways && img.HeightDots <= profile.PrintWidthDots
                ? $"Image is {img.WidthDots} dots wide but the print head is {profile.PrintWidthDots} dots. " +
                  $"Set Orientation to Landscape in the printer profile to print it across the roll ({img.HeightDots} dots wide)."
                : $"Image is {img.WidthDots} dots wide but the print head is {profile.PrintWidthDots} dots. " +
                  "The right edge will be cut off."));

        if (rowPerPage && image is { Dpi: > 0 } labelImage)
        {
            var reportMm = labelImage.HeightDots / labelImage.Dpi * 25.4;
            if (Math.Abs(reportMm - profile.Label.HeightMm) > LabelHeightToleranceMm)
                result.Add((BpMessageKind.Warning,
                    $"Label height in the report ({Cm(reportMm)} cm) differs from the printer profile ({Cm(profile.Label.HeightMm)} cm)."));
        }

        if (!rowPerPage)
            result.Add((BpMessageKind.Warning,
                "This label report uses sheet mode. Set the page mode to \"One row per page (roll)\" for roll labels."));

        // Easy to forget once set: a whole roll printed the wrong way round.
        if (profile.Raster.Mirror)
            result.Add((BpMessageKind.Information, "Mirror is on: labels print left and right swapped."));
        if (profile.Raster.Negative)
            result.Add((BpMessageKind.Information, "Negative is on: labels print white on black."));

        return result;
    }

    /// <summary>No check stops the job (the image can be made).</summary>
    public static bool CanPrint(ReportDefinition report, PrinterProfile profile)
        => Run(report, profile, null).All(r => r.Kind != BpMessageKind.Error);

    // Shown in cm, like the designer and the printer profile; kept in mm.
    private static string Cm(double mm) => (mm / 10).ToString("0.00", CultureInfo.InvariantCulture);
}
