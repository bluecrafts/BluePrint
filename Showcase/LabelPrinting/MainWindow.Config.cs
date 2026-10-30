// LabelPrinting.cfg in the window: printer profiles in the toolbar, the
// Printer Profiles dialog, and what the window remembers between runs (the
// label opened last, the profile chosen last, its size and position).

using System.IO;
using System.Windows;
using System.Windows.Controls;
using BluePrint.UI.Controls;
using LabelPrinting.Profiles;
using LabelPrinting.Views;

namespace LabelPrinting;

public partial class MainWindow
{
    private readonly AppConfigStore _store = AppConfigStore.Default;
    private AppConfig _config = new();
    private AppConfigStore.LoadReport? _loadReport;

    /// <summary>The printer profile chosen in the toolbar, or null when there is none.</summary>
    private PrinterProfile? CurrentProfile => ProfileBox.SelectedItem as PrinterProfile;

    private void LoadConfig()
    {
        try
        {
            (_config, _loadReport) = _store.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is there but cannot be read (or renamed): work with the
            // defaults, and do not save over a file we could not read.
            _config = new AppConfig();
            _loadReport = null;
            _configReadOnly = true;
            Loaded += (_, _) => BpMessage.Show(this,
                $"Could not read {AppConfigStore.FileName}.\n\n{ex.Message}\n\nPrinter profiles are not available, and nothing will be saved.",
                BpMessageKind.Error, title: AppName);
        }

        RestoreWindowPlacement(_config.App.Window);
        ShowProfiles(_config.App.SelectedProfile);
    }

    private bool _configReadOnly;

    // Tells what Load had to do, once the window is on screen to own the message.
    private void ReportConfigProblems()
    {
        if (_loadReport is null) return;

        if (_loadReport.RenamedTo is { } renamed)
            BpMessage.Show(this,
                $"{AppConfigStore.FileName} could not be read and was renamed to {Path.GetFileName(renamed)}. " +
                "Your printer profiles are in that file.",
                BpMessageKind.Warning, title: AppName);

        if (_loadReport.SkippedProfiles.Count > 0)
            BpMessage.Show(this,
                "These printer profiles could not be read and were left out:\n\n" +
                string.Join("\n", _loadReport.SkippedProfiles) +
                $"\n\nThey stay in {AppConfigStore.FileName} as they are. Fix them there, then start the application again.",
                BpMessageKind.Warning, title: AppName);
    }

    private void SaveConfig()
    {
        if (_configReadOnly) return;
        try
        {
            _store.Save(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BpMessage.Show(this,
                $"Could not save {AppConfigStore.FileName} in {_store.Folder}. " +
                $"Run the application from a folder you can write to.\n\n{ex.Message}",
                BpMessageKind.Error, title: AppName);
        }
    }

    // ---- printer profiles in the toolbar -------------------------------------

    private void ShowProfiles(string? select)
    {
        ProfileBox.ItemsSource = null;
        ProfileBox.ItemsSource = _config.PrinterProfiles;
        ProfileBox.SelectedItem =
            _config.PrinterProfiles.FirstOrDefault(p => string.Equals(p.Name, select, StringComparison.OrdinalIgnoreCase))
            ?? _config.PrinterProfiles.FirstOrDefault();
        ProfileBox.IsEnabled = _config.PrinterProfiles.Count > 0;
        ProfileBox.ToolTip = _config.PrinterProfiles.Count > 0
            ? "The printer to print to"
            : "Create a printer profile first: Print > Printer Profiles.";
    }

    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Another printer: another image, and row by row starts again.
        ResetRows();
        ScheduleRowImage();
        if (CurrentProfile is not { } profile || profile.Name == _config.App.SelectedProfile) return;
        _config.App.SelectedProfile = profile.Name;
        SaveConfig();
        SetStatus($"Printer profile: {profile.Name}.");
    }

    private void PrinterProfiles_Click(object sender, RoutedEventArgs e) => EditPrinterProfiles(tab: null);

    // The Print panel's link: how the image is printed is set in the Image tab.
    private void ChangeProfile_Click(object sender, RoutedEventArgs e) => EditPrinterProfiles(tab: "Image");

    private void EditPrinterProfiles(string? tab)
    {
        var dialog = new PrinterProfilesDialog(_config.PrinterProfiles, CurrentProfile?.Name, tab) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } profiles) return;

        _config.PrinterProfiles = profiles;
        _config.App.SelectedProfile = dialog.SelectedName ?? CurrentProfile?.Name;
        ShowProfiles(_config.App.SelectedProfile);
        ResetRows();
        ScheduleRowImage();
        SaveConfig();
        SetStatus($"Saved {profiles.Count} printer profile(s) in {AppConfigStore.FileName}.", BpMessageKind.Success);
    }

    // ---- the label opened last -----------------------------------------------

    // Inside the application's folder (the sample) it is kept relative, so the
    // folder can be moved or copied as a whole.
    private static string? ToStoredPath(string? path)
    {
        if (path is null) return null;
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }

    private static string? FromStoredPath(string? stored)
        => string.IsNullOrWhiteSpace(stored) ? null : Path.GetFullPath(stored, AppContext.BaseDirectory);

    private void RememberOpenedReport(string path)
    {
        _config.App.LastReport = ToStoredPath(path);
        SaveConfig();
    }

    // The label opened last, or the sample when there is none (or it is gone).
    private string? StartupReport()
        => FromStoredPath(_config.App.LastReport) is { } last && File.Exists(last) ? last
           : File.Exists(SampleLabelPath) ? SampleLabelPath
           : null;

    // ---- window size and position --------------------------------------------

    private void RestoreWindowPlacement(WindowPlacement? placement)
    {
        if (placement is not { Width: > 0, Height: > 0 }) return;

        // A position on a screen that is no longer there (a second monitor that
        // was unplugged) would put the window out of reach: keep the default.
        var visible = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                               SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (visible.IntersectsWith(new Rect(placement.Left, placement.Top, placement.Width, placement.Height)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
        }
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized) WindowState = WindowState.Maximized;
    }

    private void RememberWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _config.App.Window = new WindowPlacement
        {
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        RememberView();
    }
}
