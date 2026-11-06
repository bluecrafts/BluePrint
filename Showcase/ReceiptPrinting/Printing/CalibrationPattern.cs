using BluePrint.Render.Export.Mono;
using BluePrint.Render.Export.Mono.Printers;
using ReceiptPrinting.Profiles;

namespace ReceiptPrinting.Printing;

/// <summary>
/// The calibration pattern for the line spacing of the column image command
/// (ESC *): the same test block printed with five line spacings, from two below
/// the profile's to two above, each labelled with its number.
/// </summary>
/// <remarks>
/// <para>Column images are printed in passes 8 or 24 dots high, and the paper is
/// moved by the line spacing after each pass. Too much leaves white lines across
/// the black bar; too little prints the passes over each other, and the diagonal
/// lines jog at every pass. The right value gives a solid bar and straight
/// diagonals.</para>
/// <para>The pattern is drawn here, but its commands come from BluePrint's
/// encoder like every other job: one encoding per line spacing, joined.</para>
/// </remarks>
public static class CalibrationPattern
{
    /// <summary>Passes in each test block.</summary>
    public const int Passes = 4;

    /// <summary>How far each side of the profile's value the pattern goes.</summary>
    public const int Spread = 2;

    /// <summary>The line spacings printed, lowest first: the profile's value plus or minus <see cref="Spread"/>, within 1-255.</summary>
    public static IReadOnlyList<int> Spacings(int current)
        => [.. Enumerable.Range(current - Spread, 2 * Spread + 1).Where(n => n is >= 1 and <= 255)];

    /// <summary>The job: one segment per line spacing, with a pause after each but the last.</summary>
    public static IReadOnlyList<CommandSegment> Segments(PrinterProfile profile)
    {
        var escPos = profile.Commands.EscPos;
        var spacings = Spacings(escPos.ColumnLineSpacing);
        var segments = new List<CommandSegment>(spacings.Count);
        for (var i = 0; i < spacings.Count; i++)
        {
            bool first = i == 0, last = i == spacings.Count - 1;
            var options = new PrinterCommandOptions
            {
                Initialize = first && profile.Commands.Initialize,
                Copies = 1,
                EscPos =
                {
                    ImageCommand = EscPosImageCommand.Column,
                    ColumnDensity = escPos.ColumnDensity,
                    ColumnLineSpacing = spacings[i],
                    Unidirectional = escPos.Unidirectional,
                    // A line of paper between the blocks; the profile's feed and cut after the last.
                    FeedLinesAfter = last ? escPos.FeedLinesAfter : 1,
                    CutAtEnd = last && (escPos.CutAtEnd || escPos.CutAfter),
                    CutMode = escPos.CutMode,
                },
            };
            var block = Block(profile.PrintWidthDots, BandRows(escPos.ColumnDensity), spacings[i], profile.Dpi);
            segments.Add(new CommandSegment(PrinterCommands.Encode([block], PrinterLanguage.EscPos, options), Pace: !last));
        }
        return segments;
    }

    /// <summary>Dots in one pass from top to bottom.</summary>
    public static int BandRows(EscPosColumnDensity density)
        => density is EscPosColumnDensity.Single24 or EscPosColumnDensity.Double24 ? 24 : 8;

    /// <summary>
    /// One test block, <see cref="Passes"/> passes high: the number <paramref name="label"/>,
    /// a solid bar, then diagonal lines.
    /// </summary>
    public static MonochromeBitmap Block(int widthDots, int bandRows, int label, double dpi)
    {
        var height = Passes * bandRows;
        var stride = widthDots / 8;
        var pixels = new byte[stride * height];
        void Dot(int x, int y)
        {
            if ((uint)x < (uint)widthDots && (uint)y < (uint)height) pixels[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }

        // The number, as big as fits in the block, in a font of 3 x 5 cells.
        var scale = Math.Max(1, Math.Min(height / 6, widthDots / 24));
        var x0 = 0;
        foreach (var digit in label.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            var rows = Digits[digit - '0'];
            for (var r = 0; r < 5; r++)
                for (var c = 0; c < 3; c++)
                    if ((rows[r] & (4 >> c)) != 0)
                        for (var dy = 0; dy < scale; dy++)
                            for (var dx = 0; dx < scale; dx++)
                                Dot(x0 + c * scale + dx, (height - 5 * scale) / 2 + r * scale + dy);
            x0 += 4 * scale;
        }

        // The bar: a white line across it means too much line spacing.
        var barStart = x0 + scale;
        var barEnd = barStart + (widthDots - barStart) / 2;
        for (var y = 0; y < height; y++)
            for (var x = barStart; x < barEnd; x++) Dot(x, y);

        // Diagonals: a jog at each pass means the passes overlap or gap.
        var gap = Math.Max(4, height / 4);
        for (var x = barEnd + gap; x < widthDots + height; x += gap)
            for (var y = 0; y < height; y++) Dot(x - y, y);

        return MonochromeBitmap.FromPixels(widthDots, height, dpi, pixels);
    }

    // 0-9 in 3 x 5 cells, a row per entry, the left cell in bit 2.
    private static readonly int[][] Digits =
    [
        [7, 5, 5, 5, 7], [2, 6, 2, 2, 7], [7, 1, 7, 4, 7], [7, 1, 7, 1, 7], [5, 5, 7, 1, 1],
        [7, 4, 7, 1, 7], [7, 4, 7, 5, 7], [7, 1, 1, 1, 1], [7, 5, 7, 5, 7], [7, 5, 7, 1, 7],
    ];
}
