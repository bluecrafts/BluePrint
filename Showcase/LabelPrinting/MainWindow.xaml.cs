// LabelPrinting - design a label, fill it from a data file, and send it to a
// label printer as a 1-bit image wrapped in the printer's own command language
// (TSPL, ZPL, EPL or EZPL), without going through a printer driver.
//
// This file holds the window itself: title, status bar and the Help menu.
// MainWindow.Document.cs holds opening and saving the label.

using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using BluePrint.Render;
using BluePrint.UI;
using BluePrint.UI.Controls;

namespace LabelPrinting;

public partial class MainWindow : Window
{
    private const string AppName = "LabelPrinting";

    public MainWindow()
    {
        InitializeComponent();

        // The three things the merged theme cannot reach on its own: the window's
        // own font and colours, its background, and the title bar Windows draws -
        // dark in dark mode.
        BpTheme.ApplyTo(this);
        SetResourceReference(BackgroundProperty, BpKeys.SurfaceWindow);
        BpWindowChrome.Follow(this);

        // One label at a time, in this window. The designer raises OpenFileRequested
        // when its own Open command picks a file; a host with tabs would open a new
        // tab there, this one replaces the label it has (after asking).
        Designer.OpenFileRequested += path => OpenPath(path);

        Designer.IsModifiedChanged += _ => UpdateTitle();
        Designer.TitleChanged += UpdateTitle;
        UpdateTitle();

        ConfigureRetrieve();
        LoadConfig();
        InitializeElementTools();
        InitializeView();
        InitializePrinting();

        // Start on the label opened last - or the sample, so there is something
        // to look at and print the first time.
        Loaded += (_, _) =>
        {
            ReportConfigProblems();
            if (StartupReport() is { } path) OpenPath(path);
        };
    }

    private static string SamplesFolder => Path.Combine(AppContext.BaseDirectory, "Samples");

    private static string SampleLabelPath => Path.Combine(SamplesFolder, "Label2Sticker.bpt");

    private void OpenSamplesFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(SamplesFolder))
        {
            BpMessage.Show(this, $"The samples folder is missing:\n{SamplesFolder}",
                BpMessageKind.Warning, title: "Open Samples Folder");
            return;
        }
        // Explorer opens the folder; UseShellExecute is what makes a folder path
        // open in Explorer rather than fail as "not an executable".
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SamplesFolder) { UseShellExecute = true });
    }

    // "LabelPrinting - Label2Sticker.bpt*" - the star while there are unsaved changes.
    private void UpdateTitle()
    {
        if (Designer.Report is null) { Title = AppName; return; }

        var name = Designer.CurrentFilePath is { } path ? Path.GetFileName(path) : Designer.Title;
        Title = $"{AppName} - {name}{(Designer.IsModified ? "*" : "")}";
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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges()) e.Cancel = true;
        else
        {
            RememberWindowPlacement();
            SaveConfig();
        }
        base.OnClosing(e);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        BpMessage.Show(this,
            $"{AppName}\n\n" +
            "Designs labels with the BluePrint designer and prints them straight to a label printer " +
            "as a 1-bit image in the printer's own command language, with no printer driver in between.\n\n" +
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

    // Commands that change which label is open wait while the designer is
    // fetching rows: the rows would arrive for a label that is no longer there.
    //
    // WPF asks these while InitializeComponent is still building the window -
    // the toolbar buttons come before the designer in the XAML - so the designer
    // field can still be null here.
    private void NotBusy_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = Designer is { IsRetrieving: false };

    private void HasReport_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = Designer is { Report: not null, IsRetrieving: false };
}
