using System.Globalization;
using BluePrint.Core.Models;
using BluePrint.Render.Models;
using BluePrint.UI.Controls;
using ReceiptPrinting.Profiles;

namespace ReceiptPrinting.Printing;

/// <summary>
/// What is worth knowing before printing a receipt. A report that is not a
/// receipt, and a continuous receipt longer than BluePrint renders, cannot be
/// printed; the rest are notes, and the job can still be sent.
/// </summary>
/// <remarks>
/// Worked out from the laid-out receipt and the profile, without the 1-bit image:
/// the image is as wide as the page at the printer's resolution, rounded up to a
/// whole byte - the same sum BluePrint does.
/// </remarks>
public static class RasterChecks
{
    /// <summary>A receipt longer than this may not fit on what is left of a roll.</summary>
    public const double LongReceiptMm = 5000;

    /// <summary>Stretching the image more than this changes the size of the text noticeably.</summary>
    public const double StretchNoticeable = 0.10;

    public const string NotAReceipt = "This is not a receipt report. ReceiptPrinting prints canvas reports on roll paper.";

    public const string TooLong =
        "This receipt is longer than 10 m, the most a continuous form can render. Use a paged form for very long documents.";

    /// <param name="tooLong">The continuous receipt did not render: it is longer than BluePrint's limit.</param>
    /// <param name="copies">The copies of the job (the Print panel's).</param>
    public static IReadOnlyList<(BpMessageKind Kind, string Text)> Run(
        ReportType type, PrinterProfile profile, RenderedDocument? rendered, int copies, bool tooLong = false)
    {
        var result = new List<(BpMessageKind, string)>();
        if (type != ReportType.Canvas)
        {
            result.Add((BpMessageKind.Error, NotAReceipt));
            return result;
        }
        if (tooLong)
        {
            result.Add((BpMessageKind.Error, TooLong));
            return result;
        }
        if (rendered is not { Pages.Count: > 0 }) return result;

        var head = profile.PrintWidthDots;
        var natural = NaturalWidthDots(rendered.Pages[0], profile.Dpi);
        if (profile.Raster.ImageWidth == ImageWidthMode.Report)
        {
            if (natural > head)
                result.Add((BpMessageKind.Warning,
                    $"Image is {natural} dots wide but the print head is {head} dots. The right edge will be cut off."));
            else if (natural < head)
                result.Add((BpMessageKind.Information,
                    $"The receipt is {natural} dots wide on a {head}-dot print head. It prints at the left edge."));
        }
        else if (natural > 0 && Math.Abs(head - natural) / (double)natural > StretchNoticeable)
        {
            var pct = (int)Math.Round((head - (double)natural) / natural * 100);
            result.Add((BpMessageKind.Information, pct > 0
                ? $"The image is stretched {pct}% to fill the print head. Text prints larger than designed."
                : $"The image is shrunk {-pct}% to fit the print head. Text prints smaller than designed."));
        }

        var lengthMm = rendered.Pages.Sum(p => HeightPx(p) / 96.0 * 25.4);
        if (lengthMm > LongReceiptMm)
            result.Add((BpMessageKind.Information,
                $"This receipt is {(lengthMm / 1000).ToString("0.0", CultureInfo.InvariantCulture)} m long. Check that the paper roll is long enough."));

        var cutsEveryPage = profile.Commands.EscPos.CutAfter;
        if (rendered.Pages.Count > 1 && !cutsEveryPage)
            result.Add((BpMessageKind.Information, "Pages print one after another without a cut."));
        // The encoder repeats the image for each copy; only a cut after every page separates them.
        if (copies > 1 && !cutsEveryPage)
            result.Add((BpMessageKind.Information, "Copies print one after another without a cut between them."));

        return result;
    }

    /// <summary>No check stops the job.</summary>
    public static bool CanPrint(IEnumerable<(BpMessageKind Kind, string Text)> checks) => checks.All(c => c.Kind != BpMessageKind.Error);

    /// <summary>Width of the 1-bit image of a page at <paramref name="dpi"/>, as BluePrint makes it.</summary>
    public static int NaturalWidthDots(RenderedPage page, double dpi)
    {
        var widthPx = page.EffectiveWidthPx > 0 ? page.EffectiveWidthPx : page.WidthPx;
        return Math.Max(8, (int)Math.Ceiling(Math.Round(widthPx * dpi / 96.0) / 8.0) * 8);
    }

    private static double HeightPx(RenderedPage page) => page.EffectiveHeightPx > 0 ? page.EffectiveHeightPx : page.HeightPx;
}
