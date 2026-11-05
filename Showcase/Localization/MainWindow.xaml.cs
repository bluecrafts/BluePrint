// Localization - one invoice report printed in seven languages. Choose a form (each
// keeps its translations in a different way), an order of the Northwind database,
// and a language: the preview shows the invoice in that language, with the dates
// and numbers of its culture and the font of its script.
//
// This file holds the window itself: title, status bar, the File, View and Help menus.

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using BluePrint.Render;
using BluePrint.UI;
using BluePrint.UI.Controls;
using LocalizationShowcase.Forms;

namespace LocalizationShowcase;

public partial class MainWindow : Window
{
    private const string AppName = "Localization";

    // Widths the panels go back to when they are shown again.
    private double _leftWidth = 300;
    private double _rightWidth = 300;

    public MainWindow()
    {
        InitializeComponent();

        // The three things the merged theme cannot reach on its own: the window's
        // own font and colours, its background, and the title bar Windows draws -
        // dark in dark mode.
        BpTheme.ApplyTo(this);
        SetResourceReference(BackgroundProperty, BpKeys.SurfaceWindow);
        BpWindowChrome.Follow(this);

        LoadConfig();
        InitializeTheme();
        InitializePreview();
        InitializeLanguages();
        InitializeForms();
        InitializeDocuments();
        UpdateTitle();

        // The form, order and language of last time - Invoice-Embedded, 10248 and
        // English the first time - so there is an invoice to look at without a click.
        Loaded += async (_, _) =>
        {
            SelectLanguage(_config.LastLanguage);
            // Read off the UI thread, so the window is on screen at once: the first report read
            // also prepares the JSON reader of BluePrint, which takes a moment the first time.
            ShowForms(await Task.Run(() => FormCatalog.Read(SamplesFolder)));
            SelectForm(StartupForm());
            if (SelectedForm is null && _forms.FirstOrDefault(f => f.CanRead) is { } first) SelectForm(first.Path);
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        _roundCts?.Cancel();   // a document still being read is not waited for
        _retrieveTimer.Stop();
        _languageTimer.Stop();
        _searchTimer.Stop();
        RememberWindow();
        SaveConfig();
    }

    private static string SamplesFolder => Path.Combine(AppContext.BaseDirectory, "Samples");

    // "Localization - {form} - {document} - {language}". Nothing is ever edited, so there is no star.
    private void UpdateTitle()
    {
        var parts = new List<string> { AppName };
        if (SelectedForm is { } form)
        {
            parts.Add(form.Name);
            if (SelectedDocument is { } document) parts.Add(document.TitleText);
            parts.Add(CurrentLanguage.Name);
        }
        Title = string.Join(" - ", parts);
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
        Process.Start(new ProcessStartInfo(SamplesFolder) { UseShellExecute = true });
    }

    private void RefreshForms_Click(object sender, RoutedEventArgs e) => RefreshForms();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ---- View ---------------------------------------------------------------------

    private void LeftPanelMenuItem_Click(object sender, RoutedEventArgs e) => ShowLeftPanel(LeftPanelMenuItem.IsChecked);
    private void RightPanelMenuItem_Click(object sender, RoutedEventArgs e) => ShowRightPanel(RightPanelMenuItem.IsChecked);

    private void ShowLeftPanel(bool visible) => ShowPanel(LeftColumn, LeftPane, LeftSplitter, visible, ref _leftWidth);
    private void ShowRightPanel(bool visible) => ShowPanel(RightColumn, RightPane, RightSplitter, visible, ref _rightWidth);

    // A hidden panel gives its width to the preview; shown again, it comes back at the width it had.
    private static void ShowPanel(System.Windows.Controls.ColumnDefinition column, FrameworkElement pane,
                                  FrameworkElement splitter, bool visible, ref double width)
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
            column.MinWidth = 240;
        }
        pane.Visibility = splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // Light, Dark, or System (the Windows app mode). The theme changes the window
    // only: the preview is a picture of paper, so it stays white.
    private void InitializeTheme()
    {
        BpTheme.SetMode(Enum.TryParse<BpThemeMode>(_config.Theme, ignoreCase: true, out var mode)
            ? mode : BpThemeMode.System);

        // The ready-made Theme menu of BluePrint.UI, below Left Panel.
        var view = (System.Windows.Controls.MenuItem)ThemeSeparator.Parent;
        view.Items.Insert(view.Items.IndexOf(ThemeSeparator) + 1, BpThemeMenu.Create("_Theme"));

        // Remember what the user asked for (System stays System), not the light or dark it turned into.
        BpTheme.ModeChanged += _ =>
        {
            _config.Theme = BpTheme.Mode.ToString();
            SaveConfig();
        };
    }

    // ---- Help ---------------------------------------------------------------------

    private void HowLayersWork_Click(object sender, RoutedEventArgs e) =>
        BpMessage.Show(this,
            "Each text is looked up in the layers from the top down. The first layer that has the text wins. " +
            "A layer the application adds comes before the translations embedded in the report. " +
            "A text that no layer has shows the source text.",
            BpMessageKind.Information, title: "How the translation layers work");

    private void About_Click(object sender, RoutedEventArgs e) =>
        BpMessage.Show(this,
            $"{AppName}\n\n" +
            "One invoice report printed in seven languages. Each form keeps its translations in a different way: " +
            "inside the report, in files next to it, in a glossary shared by two reports, or in a source of the application.\n\n" +
            $"BluePrint {PackageVersion()}",
            BpMessageKind.Information, title: $"About {AppName}");

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
