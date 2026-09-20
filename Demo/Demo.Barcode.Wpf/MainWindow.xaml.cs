// Every symbology, on screen, re-encoded as you type.
//
//   dotnet run --project Demo.Barcode.Wpf
//
// The window itself is ordinary WPF. What is worth reading is how little there
// is between a string and a symbol on screen:
//
//   BarcodeEncoders.Default.Encode(...)   turns the value into a module grid
//   BarcodeView                           draws that grid (see BarcodeView.cs)
//
// Nothing in between renders an image, and nothing caches one. Saving a PNG
// goes through BluePrint.Barcode.Render and SkiaSharp, and saving an SVG goes
// through the encoder alone, so both paths are here side by side.

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BluePrint.Barcode;
using BluePrint.Barcode.Layout;
using BluePrint.Barcode.Output;
using BluePrint.Barcode.Render;
using Demo.Shared;
using Microsoft.Win32;
using SkiaSharp;

namespace Demo.Barcode.Wpf;

/// <summary>One card on the second tab.</summary>
public sealed record GalleryItem(string Title, string Shape, string Note, BarcodeSymbol Symbol, bool ShowHri);

public partial class MainWindow : Window
{
    private const float HriSizePx = 15f;

    /// <summary>
    /// The renderer never picks a font. It is handed one, and whoever hands it
    /// over keeps it alive, so one typeface is opened here and disposed at the
    /// end rather than opened for every save.
    /// </summary>
    private readonly SKTypeface _hriTypeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;

    private BarcodeEncodeResult _result;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();

        var grouped = new CollectionViewSource { Source = BarcodeSamples.All };
        grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BarcodeSample.Family)));
        SymbologyList.ItemsSource = grouped.View;

        GalleryList.ItemsSource = BuildGallery();

        _ready = true;
        SymbologyList.SelectedIndex = 0;
    }

    private BarcodeSample? Current => SymbologyList.SelectedItem as BarcodeSample;

    // ---- the two views ------------------------------------------------------

    private void Views_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExplorePage is null || GalleryPage is null) return;

        bool explore = Views.SelectedIndex == 0;
        ExplorePage.Visibility = explore ? Visibility.Visible : Visibility.Collapsed;
        GalleryPage.Visibility = explore ? Visibility.Collapsed : Visibility.Visible;
    }

    private static List<GalleryItem> BuildGallery()
    {
        var items = new List<GalleryItem>();

        foreach (var sample in BarcodeSamples.All)
        {
            var result = BarcodeEncoders.Default.Encode(sample.Symbology, sample.Data, sample.EncodeOptions);
            if (result.Symbol is not { } symbol) continue;

            items.Add(new GalleryItem(
                sample.Title,
                $"{sample.Family} - {symbol.Rows} x {symbol.Columns} modules",
                sample.Note,
                symbol,
                sample.ShowsHri(symbol)));
        }

        return items;
    }

    // ---- encode, then draw --------------------------------------------------

    private void SymbologyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Current is not { } sample) return;

        _ready = false;
        DataBox.Text = sample.Data;
        EcBox.SelectedIndex = (int)sample.EncodeOptions.ErrorCorrection;
        EcBox.IsEnabled = sample.Symbology == BarcodeSymbology.QRCode;
        _ready = true;

        Refresh(resetHri: true);
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => Refresh();

    private void ResetValue_Click(object sender, RoutedEventArgs e)
    {
        if (Current is { } sample) DataBox.Text = sample.Data;
    }

    private void Refresh(bool resetHri = false)
    {
        if (!_ready || Current is not { } sample) return;

        // The sample carries the options that make its symbology interesting -
        // the column count for PDF417, for instance - and the panel overrides
        // only the one the person can reach.
        var options = new BarcodeEncodeOptions
        {
            ErrorCorrection = (BarcodeEcLevel)Math.Max(0, EcBox.SelectedIndex),
            Pdf417Columns = sample.EncodeOptions.Pdf417Columns,
            Encoding = sample.EncodeOptions.Encoding,
        };

        _result = BarcodeEncoders.Default.Encode(sample.Symbology, DataBox.Text, options);

        if (resetHri && _result.Symbol is { } fresh)
        {
            _ready = false;
            HriCheck.IsChecked = sample.ShowsHri(fresh);
            _ready = true;
        }

        // A failed encode clears the symbol. Drawing the last good one instead
        // would be the worst of both: the screen would show a symbol that the
        // value on screen does not produce.
        Preview.Symbol = _result.Symbol;
        Preview.ShowHri = HriCheck.IsChecked == true;
        Preview.ModuleSize = FitCheck.IsChecked == true ? 0 : ModuleSlider.Value;
        PreviewRotation.Angle = SelectedAngle();

        ModuleSlider.IsEnabled = FitCheck.IsChecked != true;
        ModuleValue.Text = FitCheck.IsChecked == true
            ? "fit"
            : ((int)ModuleSlider.Value).ToString(CultureInfo.InvariantCulture);

        ApplyInk();

        SampleNote.Text = sample.Note;
        SampleHri.Text = _result.Symbol?.HriText is { Length: > 0 } hri
            ? "Under the bars: " + hri
            : "This symbology has nothing to print under the bars.";

        PreviewMessage.Text = _result.Message;
        PreviewMessage.Visibility = _result.IsOk ? Visibility.Collapsed : Visibility.Visible;

        bool ok = _result.IsOk;
        SavePngButton.IsEnabled = ok;
        SaveSvgButton.IsEnabled = ok;
        CopyButton.IsEnabled = ok;

        UpdateStatus(sample);
    }

    private void UpdateStatus(BarcodeSample sample)
    {
        if (_result.Symbol is { } symbol)
        {
            StatusText.Foreground = (Brush)FindResource("Ink");
            StatusText.Text = $"{sample.Title} encoded.";
            StatusDetail.Text =
                $"{(symbol.Kind == SymbolKind.Matrix2D ? "2D" : "linear")}   " +
                $"{symbol.Rows} x {symbol.Columns} modules   " +
                $"quiet zone {symbol.QuietZoneLeft}/{symbol.QuietZoneRight}";
        }
        else
        {
            // Not an exception and not an empty box: the status says which of
            // the value's problems stopped it, in the words the library used.
            StatusText.Foreground = (Brush)FindResource("Danger");
            StatusText.Text = $"{_result.Status} - {_result.Message}";
            StatusDetail.Text = "Nothing is drawn, on purpose.";
        }
    }

    // ---- appearance ---------------------------------------------------------

    private void ApplyInk()
    {
        Brush bars;
        Brush paper;

        switch (InkBox.SelectedIndex)
        {
            case 1:
                bars = new SolidColorBrush(Color.FromRgb(0x12, 0x2A, 0x4F));
                paper = Brushes.White;
                break;
            case 2:
                bars = Brushes.White;
                paper = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x18));
                break;
            default:
                bars = Brushes.Black;
                paper = Brushes.White;
                break;
        }

        Preview.BarBrush = bars;
        Preview.PaperBrush = paper;
        PaperSurface.Background = paper;
    }

    private double SelectedAngle() =>
        RotationBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
        double.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out double angle)
            ? angle
            : 0;

    // ---- saving -------------------------------------------------------------

    private void SavePng_Click(object sender, RoutedEventArgs e)
    {
        if (_result.Symbol is not { } symbol || Current is not { } sample) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save as PNG",
            Filter = "PNG image|*.png",
            FileName = sample.FileStem + ".png",
        };

        if (dialog.ShowDialog(this) != true) return;

        int module = SaveModuleSize();
        bool showHri = Preview.ShowHri && !string.IsNullOrEmpty(symbol.HriText);
        int width = TotalColumns(symbol) * module;
        int height = BarHeight(symbol, module) + (showHri ? (int)(HriSizePx * 1.6f) : 0);

        using var render = SkiaBarcodeRenderer.Render(new BarcodeRenderRequest
        {
            Symbol = symbol,
            PixelWidth = width,
            PixelHeight = height,
            FixedModuleSize = module,
            ShowHri = showHri,
            HriTypeface = _hriTypeface,
            HriSizePx = HriSizePx,
            Rotation = SelectedRotation(),
            Foreground = ToSkColor(Preview.BarBrush, SKColors.Black),
            Background = ToSkColor(Preview.PaperBrush, SKColors.White),
        });

        if (!render.IsOk)
        {
            Warn($"{render.Status} - {render.Message}");
            return;
        }

        File.WriteAllBytes(dialog.FileName, BarcodePng.Encode(render.Bitmap!));
        Report($"Wrote {dialog.FileName} at {width} x {height} pixels.");
    }

    private void SaveSvg_Click(object sender, RoutedEventArgs e)
    {
        if (_result.Symbol is not { } symbol || Current is not { } sample) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save as SVG",
            Filter = "SVG image|*.svg",
            FileName = sample.FileStem + ".svg",
        };

        if (dialog.ShowDialog(this) != true) return;

        int module = SaveModuleSize();

        // Snap is None: an SVG is turned into pixels by whoever opens it, at a
        // resolution nobody here knows, so rounding the geometry now would only
        // throw away precision that is still needed.
        var layout = BarcodeLayoutBuilder.Fit(symbol, TotalColumns(symbol) * module, BarHeight(symbol, module),
            new BarcodeLayoutOptions
            {
                Snap = BarcodeSnapMode.None,
                FixedModuleSize = module,
            });

        if (layout is null)
        {
            Warn("The symbol does not fit at that module size.");
            return;
        }

        File.WriteAllText(dialog.FileName, BarcodeSvgWriter.Write(layout, new BarcodeSvgOptions
        {
            Foreground = ToCss(Preview.BarBrush, "#000000"),
            Background = ToCss(Preview.PaperBrush, "#ffffff"),
        }));

        Report($"Wrote {dialog.FileName}. An SVG carries the bars; the readable line is text the host draws in its own font.");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (PaperSurface.ActualWidth <= 0 || PaperSurface.ActualHeight <= 0) return;

        // Straight off the screen this time, so what lands on the clipboard is
        // the vector drawing rasterised at the display scale in use.
        var dpi = VisualTreeHelper.GetDpi(PaperSurface);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(PaperSurface.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(PaperSurface.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        bitmap.Render(PaperSurface);
        Clipboard.SetImage(bitmap);

        Report("Copied at the size shown. Save a PNG instead to choose the module size.");
    }

    private int SaveModuleSize() => FitCheck.IsChecked == true ? 4 : (int)ModuleSlider.Value;

    private static int TotalColumns(BarcodeSymbol symbol) =>
        symbol.Columns + symbol.QuietZoneLeft + symbol.QuietZoneRight;

    /// <summary>
    /// A 2D symbol has to keep its proportions; the height of a linear one
    /// means nothing to a scanner, so it is simply tall enough to aim at.
    /// </summary>
    private static int BarHeight(BarcodeSymbol symbol, int module) =>
        symbol.Kind == SymbolKind.Matrix2D
            ? (symbol.Rows + 2 * symbol.QuietZoneY) * module
            : 28 * module;

    private BarcodeRotation SelectedRotation() => SelectedAngle() switch
    {
        90 => BarcodeRotation.Rotate90,
        180 => BarcodeRotation.Rotate180,
        270 => BarcodeRotation.Rotate270,
        _ => BarcodeRotation.None,
    };

    private static SKColor ToSkColor(Brush brush, SKColor fallback) =>
        brush is SolidColorBrush solid
            ? new SKColor(solid.Color.R, solid.Color.G, solid.Color.B)
            : fallback;

    private static string ToCss(Brush brush, string fallback) =>
        brush is SolidColorBrush solid
            ? $"#{solid.Color.R:x2}{solid.Color.G:x2}{solid.Color.B:x2}"
            : fallback;

    private void Report(string message)
    {
        StatusText.Foreground = (Brush)FindResource("Ink");
        StatusText.Text = message;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Cannot draw the symbol", MessageBoxButton.OK, MessageBoxImage.Warning);

    protected override void OnClosed(EventArgs e)
    {
        _hriTypeface.Dispose();
        base.OnClosed(e);
    }
}
