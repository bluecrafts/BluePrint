// ReceiptPrinting - open a receipt form, fill it from the Northwind database with
// retrieval arguments, and send it to a receipt printer as a 1-bit image wrapped
// in ESC/POS, without going through a printer driver.
//
// This file holds the window itself: title, status bar, the View and Help menus.

using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BluePrint.Render;
using BluePrint.UI;
using BluePrint.UI.Controls;

namespace ReceiptPrinting;

public partial class MainWindow : Window
{
    private const string AppName = "ReceiptPrinting";

    // Widths the panels go back to when they are shown again.
    private double _leftWidth = 280;
    private double _rightWidth = 320;

    public MainWindow()
    {
        InitializeComponent();

        // The three things the merged theme cannot reach on its own: the window's
        // own font and colours, its background, and the title bar Windows draws -
        // dark in dark mode.
        BpTheme.ApplyTo(this);
        SetResourceReference(BackgroundProperty, BpKeys.SurfaceWindow);
        BpWindowChrome.Follow(this);

        InitializePreview();
        LoadConfig();
        InitializeTheme();
        InitializeForms();
        InitializeDocuments();
        UpdateTitle();

        // Open the receipt of last time on its own - the first sample the first time -
        // so there is something to look at without a click.
        Loaded += (_, _) =>
        {
            ReportConfigProblems();
            SelectDocument(StartupDocument());
            SelectForm(StartupForm());
            if (SelectedForm is null && _forms.FirstOrDefault(f => f.CanRead) is { } first) SelectForm(first.Path);
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!e.Cancel && !ConfirmCloseWhilePrinting()) e.Cancel = true;
        if (!e.Cancel)
        {
            _roundCts?.Cancel();   // a receipt still rendering is not waited for
            _retrieveTimer.Stop();  // nor one that was about to start
            _searchTimer.Stop();
            RememberWindow();
            SaveConfig();
        }
        base.OnClosing(e);
    }

    private static string SamplesFolder => Path.Combine(AppContext.BaseDirectory, "Samples");

    // "ReceiptPrinting - {form} - {document}". Nothing is ever edited, so there is no star.
    private void UpdateTitle(string? form = null, string? document = null)
    {
        Title = form is null ? AppName
              : document is null ? $"{AppName} - {form}"
              : $"{AppName} - {form} - {document}";
    }

    // The status bar holds the last thing that happened. There is no log: each
    // message replaces the previous one.
    private void SetStatus(string text, BpMessageKind kind = BpMessageKind.Information)
    {
        StatusText.Text = text;
        StatusPanel.ToolTip = text;
        StatusIcon.Kind = kind;
        StatusIcon.Visibility = Visibility.Visible;
    }

    // ---- File ---------------------------------------------------------------------

    private void OpenSamplesFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(SamplesFolder))
        {
            BpMessage.Show(this, $"The samples folder is missing:\n{SamplesFolder}",
                BpMessageKind.Warning, title: "Open Samples Folder");
            return;
        }
        // UseShellExecute is what makes a folder path open in Explorer rather than
        // fail as "not an executable".
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SamplesFolder) { UseShellExecute = true });
    }

    private void RefreshForms_Click(object sender, RoutedEventArgs e) => RefreshForms();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ---- View ---------------------------------------------------------------------

    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) => Monochrome.StepZoom(+1);
    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) => Monochrome.StepZoom(-1);

    private void PanelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(LeftColumn, LeftPane, LeftSplitter, LeftPanelMenuItem.IsChecked, minWidth: 220, ref _leftWidth);
        ShowPanel(RightColumn, RightPane, RightSplitter, RightPanelMenuItem.IsChecked, minWidth: 280, ref _rightWidth);
    }

    // A hidden panel gives its width to the preview; shown again, it comes back at
    // the width it had.
    private static void ShowPanel(ColumnDefinition column, FrameworkElement pane, GridSplitter splitter,
                                  bool visible, double minWidth, ref double width)
    {
        bool shown = pane.Visibility == Visibility.Visible;
        if (visible == shown) return;

        if (!visible)
        {
            width = column.ActualWidth;
            column.MinWidth = 0;
            column.Width = new GridLength(0);
        }
        else
        {
            column.Width = new GridLength(width);
            column.MinWidth = minWidth;
        }
        pane.Visibility = splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // Light, Dark, or System (the Windows app mode). Nothing switches the BluePrint
    // theme on its own: it stays light until the application calls SetMode.
    private void InitializeTheme()
    {
        BpTheme.SetMode(Enum.TryParse<BpThemeMode>(_config.App.Theme, ignoreCase: true, out var mode)
            ? mode : BpThemeMode.System);

        // The ready-made Theme menu of BluePrint.UI, below Right Panel.
        var view = (MenuItem)ThemeSeparator.Parent;
        view.Items.Insert(view.Items.IndexOf(ThemeSeparator) + 1, BpThemeMenu.Create("_Theme"));

        // Remember what the user asked for (System stays System), not the light or
        // dark it turned into.
        BpTheme.ModeChanged += _ =>
        {
            _config.App.Theme = BpTheme.Mode.ToString();
            SaveConfig();
        };
    }

    // ---- Help ---------------------------------------------------------------------

    private void About_Click(object sender, RoutedEventArgs e)
    {
        BpMessage.Show(this,
            $"{AppName}\n\n" +
            "Opens receipt forms, fills them from a database and prints them straight to a receipt printer " +
            "as a 1-bit image in ESC/POS, with no printer driver in between.\n\n" +
            $"BluePrint {PackageVersion()}",
            BpMessageKind.Information, title: $"About {AppName}");
    }

    // The version of the BluePrint package this build uses, without the source
    // revision that the informational version carries after a '+'.
    private static string PackageVersion()
    {
        var assembly = typeof(BluePrintDocument).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "(unknown)";
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
