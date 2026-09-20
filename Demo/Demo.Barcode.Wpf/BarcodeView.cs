// A BluePrint symbol drawn by WPF itself, with no imaging step in between.
//
// This is the piece worth copying into a real application. A BarcodeSymbol is a
// grid of modules with no unit attached to it, so the same symbol can be drawn
// at any size by anything that can fill a rectangle. Here that is WPF, which
// means the result is vector: it stays sharp when the window is resized, when
// the display scale changes, and when it goes to a printer.
//
// Two rules are worth keeping whatever draws:
//
//   the quiet zone is part of the symbol. It has to be filled with the paper
//   colour, not left transparent, or a dark background behind it stops the
//   symbol from scanning.
//
//   never squeeze it. BarcodeLayoutBuilder.Fit returns null rather than draw
//   modules too small to read, and that answer is meant to be shown to the
//   person, not swallowed.

using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using BluePrint.Barcode;
using BluePrint.Barcode.Layout;

namespace Demo.Barcode.Wpf;

public sealed class BarcodeView : FrameworkElement
{
    /// <summary>Height of the readable line as a multiple of its font size.</summary>
    private const double HriLineHeightFactor = 1.6;

    /// <summary>Guard bars reach five modules down past the digits, per ISO/IEC 15420.</summary>
    private const int GuardExtensionModules = 5;

    /// <summary>A linear symbol is never drawn shorter than this, whatever the ratio works out to.</summary>
    private const double MinLinearBarHeight = 44;

    public static readonly DependencyProperty SymbolProperty = Register<BarcodeSymbol?>(nameof(Symbol), null);
    public static readonly DependencyProperty ShowHriProperty = Register(nameof(ShowHri), true);
    public static readonly DependencyProperty ModuleSizeProperty = Register(nameof(ModuleSize), 0d);
    public static readonly DependencyProperty HriFontSizeProperty = Register(nameof(HriFontSize), 14d);
    public static readonly DependencyProperty BarBrushProperty = Register<Brush>(nameof(BarBrush), Brushes.Black);
    public static readonly DependencyProperty PaperBrushProperty = Register<Brush>(nameof(PaperBrush), Brushes.White);
    public static readonly DependencyProperty MessageBrushProperty = Register<Brush>(nameof(MessageBrush), Brushes.Gray);
    public static readonly DependencyProperty LinearHeightRatioProperty = Register(nameof(LinearHeightRatio), 0.3);

    /// <summary>What to draw. Null draws nothing.</summary>
    public BarcodeSymbol? Symbol
    {
        get => (BarcodeSymbol?)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    /// <summary>Put the readable text of the symbol under the bars.</summary>
    public bool ShowHri
    {
        get => (bool)GetValue(ShowHriProperty);
        set => SetValue(ShowHriProperty, value);
    }

    /// <summary>Module size in device independent units. Zero fills the element.</summary>
    public double ModuleSize
    {
        get => (double)GetValue(ModuleSizeProperty);
        set => SetValue(ModuleSizeProperty, value);
    }

    public double HriFontSize
    {
        get => (double)GetValue(HriFontSizeProperty);
        set => SetValue(HriFontSizeProperty, value);
    }

    /// <summary>The dark modules.</summary>
    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>Behind the symbol and its quiet zone. Keep it light.</summary>
    public Brush PaperBrush
    {
        get => (Brush)GetValue(PaperBrushProperty);
        set => SetValue(PaperBrushProperty, value);
    }

    /// <summary>Used for the line shown when the symbol cannot be drawn.</summary>
    public Brush MessageBrush
    {
        get => (Brush)GetValue(MessageBrushProperty);
        set => SetValue(MessageBrushProperty, value);
    }

    /// <summary>
    /// How tall a linear symbol is drawn, as a fraction of how wide it came out.
    /// </summary>
    /// <remarks>
    /// The height of a linear symbol carries no information: a scanner reads one
    /// line across it and the rest is there so the line can be aimed. So this is
    /// a matter of looks, not of correctness, and it is a ratio rather than a
    /// number of units because a symbol that grew wider should grow taller too.
    /// </remarks>
    public double LinearHeightRatio
    {
        get => (double)GetValue(LinearHeightRatioProperty);
        set => SetValue(LinearHeightRatioProperty, value);
    }

    /// <summary>
    /// Why the last draw produced nothing, or null when it drew. Read it after
    /// a layout pass to show the reason next to the symbol.
    /// </summary>
    public string? FitMessage { get; private set; }

    protected override void OnRender(DrawingContext dc)
    {
        FitMessage = null;

        var symbol = Symbol;
        if (symbol is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        string? hriText = ShowHri ? symbol.HriText : null;
        bool drawHri = !string.IsNullOrEmpty(hriText);
        double hriBand = drawHri ? HriFontSize * HriLineHeightFactor : 0;
        double barArea = ActualHeight - hriBand;

        if (barArea <= 0)
        {
            DrawMessage(dc, "No room left for the symbol under the text.");
            return;
        }

        // Snap is None because WPF works in device independent units and turns
        // them into pixels at the very end, the way a rasteriser resolves an
        // SVG. Rounding the geometry here as well would round it twice.
        var options = new BarcodeLayoutOptions
        {
            Snap = BarcodeSnapMode.None,
            MinModuleSize = 0.2,
            FixedModuleSize = ModuleSize > 0 ? ModuleSize : null,
        };

        var layout = BarcodeLayoutBuilder.Fit(symbol, ActualWidth, barArea, options);

        if (layout is null)
        {
            DrawMessage(dc, "Too small to draw. Lower the module size, or make the window bigger.");
            return;
        }

        // How wide a linear symbol comes out depends only on the width it was
        // given, so asking a second time with a shorter box returns the same
        // modules at a sensible height instead of bars stretched down the whole
        // of a tall element.
        if (symbol.Kind == SymbolKind.Linear1D)
        {
            double preferred = Math.Max(MinLinearBarHeight, layout.Width * LinearHeightRatio);
            if (preferred < barArea)
            {
                barArea = preferred;
                layout = BarcodeLayoutBuilder.Fit(symbol, ActualWidth, barArea, options) ?? layout;
            }
        }

        // Whatever is left over goes above and below in equal measure, so a
        // short symbol sits in the middle of the space rather than at the top.
        double slack = Math.Max(0, ActualHeight - hriBand - barArea) / 2;
        if (slack > 0) dc.PushTransform(new TranslateTransform(0, slack));

        // The quiet zone belongs to the symbol, so the paper colour goes under
        // all of it, readable line included.
        dc.DrawRectangle(PaperBrush, null,
            new Rect(layout.OffsetX, layout.OffsetY, layout.Width, layout.Height + hriBand));

        var guards = symbol.HriLayout?.ExtendedGuards;
        double guardExtension = drawHri && guards is not null
            ? Math.Min(GuardExtensionModules * layout.ModuleSize, hriBand)
            : 0;

        // Bars already merges runs of dark modules, so this loop stays short
        // even for a dense 2D symbol: one rectangle per run, not per module.
        foreach (var bar in layout.Bars)
        {
            double extra = guardExtension > 0 && guards is not null && IsGuard(bar, layout, guards)
                ? guardExtension
                : 0;

            dc.DrawRectangle(BarBrush, null, new Rect(bar.X, bar.Y, bar.Width, bar.Height + extra));
        }

        if (drawHri)
        {
            var band = new Rect(layout.OffsetX, layout.OffsetY + layout.Height, layout.Width, hriBand);

            if (symbol.HriLayout is { } structured)
                DrawStructuredHri(dc, layout, structured, band);
            else
                DrawCentredHri(dc, hriText!, band);
        }

        if (slack > 0) dc.Pop();
    }

    /// <summary>
    /// EAN and UPC do not print one string under the symbol. The digits are
    /// split into groups, each centred under the part of the symbol that
    /// carries it, and the first digit of an EAN-13 sits outside the bars
    /// altogether because parity encodes it rather than a bar pattern.
    /// </summary>
    private void DrawStructuredHri(DrawingContext dc, BarcodeLayout layout, BarcodeHriLayout hri, Rect band)
    {
        foreach (var segment in hri.Segments)
        {
            if (segment.Text.Length == 0) continue;

            double left = layout.ModuleOriginX + segment.StartModule * layout.ModuleSize;
            double right = layout.ModuleOriginX + segment.EndModule * layout.ModuleSize;

            var text = Format(segment.Text, BarBrush);
            dc.DrawText(text, new Point(
                left + Math.Max(0, (right - left - text.Width) / 2),
                band.Top + Math.Max(0, (band.Height - text.Height) / 2)));
        }
    }

    private void DrawCentredHri(DrawingContext dc, string hriText, Rect band)
    {
        var text = Format(hriText, BarBrush);

        // A value can be wider than the symbol carrying it - a GS1 string under
        // a small DataMatrix, for one. Shrinking it to fit keeps it inside the
        // quiet zone instead of letting it run out over whatever the symbol
        // happens to have been placed on.
        if (band.Width > 0 && text.Width > band.Width)
            text = Format(hriText, BarBrush, Math.Max(7, HriFontSize * band.Width / text.Width));

        dc.DrawText(text, new Point(
            band.Left + Math.Max(0, (band.Width - text.Width) / 2),
            band.Top + Math.Max(0, (band.Height - text.Height) / 2)));
    }

    private void DrawMessage(DrawingContext dc, string message)
    {
        FitMessage = message;

        var text = Format(message, MessageBrush);
        text.MaxTextWidth = Math.Max(40, ActualWidth - 24);
        text.TextAlignment = TextAlignment.Center;

        dc.DrawText(text, new Point(
            Math.Max(0, (ActualWidth - text.Width) / 2),
            Math.Max(0, (ActualHeight - text.Height) / 2)));
    }

    /// <summary>Which module does this rectangle start at, and is that inside a guard?</summary>
    private static bool IsGuard(BarRect bar, BarcodeLayout layout, IReadOnlyList<HriGuard> guards)
    {
        int start = (int)Math.Round((bar.X - layout.ModuleOriginX) / layout.ModuleSize);
        int end = start + (int)Math.Round(bar.Width / layout.ModuleSize);

        foreach (var guard in guards)
            if (start >= guard.StartModule && end <= guard.EndModule) return true;

        return false;
    }

    private FormattedText Format(string text, Brush brush, double? size = null) => new(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size ?? HriFontSize,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(BarcodeView),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
