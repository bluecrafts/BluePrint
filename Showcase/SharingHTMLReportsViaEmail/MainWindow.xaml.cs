// Sharing HTML Reports via Email - a monthly report of the Northwind database, sent
// as HTML in the body of an email. Choose a report and a month: the preview shows the
// report as it prints, and the Email tab the message as it is sent. On the right, who
// gets it, how the HTML is laid out, and the buttons that send it.
//
// This file holds the window itself: the menus, the side panels, the status bar.

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using BluePrint.Render;
using BluePrint.UI;
using BluePrint.UI.Controls;

namespace SharingHtmlReports;

public partial class MainWindow : Window
{
    private const string AppName = "Sharing HTML Reports via Email";

    // Widths the panels go back to when they are shown again.
    private double _leftWidth = 280;
    private double _rightWidth = 320;

    public MainWindow()
    {
        InitializeComponent();

        // The three things the merged theme cannot reach on its own: the window's
        // own font and colours, its background, and the title bar Windows draws.
        BpTheme.ApplyTo(this);
        SetResourceReference(BackgroundProperty, BpKeys.SurfaceWindow);
        BpWindowChrome.Follow(this);

        LoadConfig();
        InitializeTheme();
        InitializePreview();
        InitializeEmail();
        InitializeMonths();

        // The report and month of last time - the first report and April 1998 the first
        // time - so there is something to look at without a click.
        Loaded += async (_, _) =>
        {
            ShowReports(await Task.Run(() => Reports.ReportCatalog.Read(SamplesFolder)));
            SelectReport(_config.LastReport);
            await LoadMonthsAsync();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _sendCts is not null)
            {
                _sendCts.Cancel();
                e.Handled = true;
            }
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        _roundCts?.Cancel();     // a month still being read is not waited for
        _emailCts?.Cancel();
        _sendCts?.Cancel();
        _retrieveTimer.Stop();
        _emailTimer.Stop();
        CommitFields();
        RememberWindow();
        SaveConfig();
    }

    private static string SamplesFolder => Path.Combine(AppContext.BaseDirectory, "Samples");

    // The status bar holds the last thing that happened. Each message replaces the one before.
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
        // UseShellExecute is what makes a folder path open in Explorer.
        Process.Start(new ProcessStartInfo(SamplesFolder) { UseShellExecute = true });
    }

    private async void RefreshReports_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedReport?.FileName;
        ShowReports(await Task.Run(() => Reports.ReportCatalog.Read(SamplesFolder)));
        SelectReport(selected);
        SetStatus($"Found {_reports.Count} report(s) in the Samples folder.");
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ---- View ---------------------------------------------------------------------

    private void ShowReportTab_Executed(object sender, ExecutedRoutedEventArgs e) => PreviewTabs.SelectedItem = ReportTab;
    private void ShowEmailTab_Executed(object sender, ExecutedRoutedEventArgs e) => PreviewTabs.SelectedItem = EmailTab;

    private void LeftPanelMenuItem_Click(object sender, RoutedEventArgs e) => ShowLeftPanel(LeftPanelMenuItem.IsChecked);
    private void RightPanelMenuItem_Click(object sender, RoutedEventArgs e) => ShowRightPanel(RightPanelMenuItem.IsChecked);

    private void ShowLeftPanel(bool visible) => ShowPanel(LeftColumn, LeftPane, LeftSplitter, visible, ref _leftWidth, 220);
    private void ShowRightPanel(bool visible) => ShowPanel(RightColumn, RightPane, RightSplitter, visible, ref _rightWidth, 280);

    // A hidden panel gives its width to the preview; shown again, it comes back at the width it had.
    private static void ShowPanel(System.Windows.Controls.ColumnDefinition column, FrameworkElement pane,
                                  FrameworkElement splitter, bool visible, ref double width, double minWidth)
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

    // Light, Dark, or System (the Windows app mode). The theme changes the window only:
    // the preview is a picture of paper and the email is what the recipient sees.
    private void InitializeTheme()
    {
        BpTheme.SetMode(Enum.TryParse<BpThemeMode>(_config.Theme, ignoreCase: true, out var mode)
            ? mode : BpThemeMode.System);

        var view = (System.Windows.Controls.MenuItem)ThemeSeparator.Parent;
        view.Items.Insert(view.Items.IndexOf(ThemeSeparator) + 1, BpThemeMenu.Create("_Theme"));

        BpTheme.ModeChanged += _ =>
        {
            _config.Theme = BpTheme.Mode.ToString();
            SaveConfig();
        };
    }

    // ---- Help ---------------------------------------------------------------------

    private void LayoutHelp_Click(object sender, RoutedEventArgs e) =>
        BpMessage.Show(this,
            "Mail programs are not web browsers. Gmail and Outlook for Windows remove style sheets " +
            "and do not support CSS positioning.\n\n" +
            "Styled table: shows the same in every mail program. Each cell carries its own styles, " +
            "so long reports make big messages.\n\n" +
            "Plain table: also works everywhere, and is the smallest, but has no colours or lines.\n\n" +
            "Absolute: like the PDF in Apple Mail, Thunderbird and web browsers; the layout is lost " +
            "in Gmail and Outlook for Windows.\n\n" +
            "Gmail shows only the first 102 KB of a message. Attach the report as an .html file " +
            "when the full layout matters.",
            BpMessageKind.Information, title: "Which layout works in which mail program");

    private void About_Click(object sender, RoutedEventArgs e) =>
        BpMessage.Show(this,
            $"{AppName}\n\n" +
            "A monthly report of the Northwind database, exported to HTML with inline styles and sent " +
            "in the body of an email.\n\n" +
            $"BluePrint {PackageVersion()}",
            BpMessageKind.Information, title: $"About {AppName}");

    // The version of the BluePrint package this build uses, without the source revision after a '+'.
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
