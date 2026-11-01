// ReceiptPrinting.cfg in the window: the printer profiles in the toolbar and in
// the Printer Profile panel (one list, one choice), and what the window remembers
// between runs - the form, document and profile chosen last, the theme, the size
// and position of the window and its panels.
//
// The profiles are saved by the panel as soon as a field is committed. The rest
// is written when it changes (profile, form, document, theme) and when the
// window closes (size, position, panels, zoom).

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BluePrint.UI.Controls;
using ReceiptPrinting.Printing;
using ReceiptPrinting.Profiles;

namespace ReceiptPrinting;

public partial class MainWindow
{
    private readonly AppConfigStore _store = AppConfigStore.Default;
    private AppConfig _config = new();
    private AppConfigStore.LoadReport? _loadReport;
    private readonly ObservableCollection<PrinterProfile> _profiles = [];

    // The file was there but could not be read: nothing is saved over it.
    private bool _configReadOnly;

    // A failed save is reported once, not again for every field after it, until a save works.
    private bool _saveErrorShown;

    /// <summary>The printer profile chosen in the toolbar and in the panel, or null when there is none.</summary>
    private PrinterProfile? CurrentProfile => ProfilePanel.Selected;

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

        foreach (var profile in _config.PrinterProfiles) _profiles.Add(profile);

        RestoreWindowPlacement(_config.App.Window);
        RestorePanels();
        Monochrome.ZoomText = _config.App.PreviewZoom;

        ProfilePanel.SaveConfig = SaveConfig;
        ProfilePanel.ExpandedGroups = _config.App.Expanded;
        ProfilePanel.SelectedChanged += (_, _) => OnProfileChosen();
        ProfilePanel.ProfileChanged += (_, _) => OnProfileEdited();
        ProfilePanel.ListChanged += (_, _) => ShowProfileList();
        ProfilePanel.ProblemsChanged += (_, _) => ShowPrintState();

        ProfilePanel.Attach(_profiles, _config.App.SelectedProfile);
        ShowProfileList();
    }

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

    /// <summary>Writes ReceiptPrinting.cfg; false (after telling the user, once) when it cannot be written.</summary>
    private bool SaveConfig()
    {
        if (_configReadOnly) return false;
        try
        {
            _config.PrinterProfiles = [.. _profiles];
            _config.App.SelectedProfile = CurrentProfile?.Name ?? _config.App.SelectedProfile;
            _store.Save(_config);
            _saveErrorShown = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_saveErrorShown)
            {
                _saveErrorShown = true;
                BpMessage.Show(this,
                    $"Could not save {AppConfigStore.FileName} in {_store.Folder}. " +
                    $"Run the application from a folder you can write to.\n\n{ex.Message}",
                    BpMessageKind.Error, title: AppName);
            }
            return false;
        }
    }

    // ---- the printer profile: toolbar and panel ----------------------------------

    // The toolbar box shows the panel's choice; it is never the other way round
    // except when the user picks in the toolbar.
    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is PrinterProfile chosen && _profiles.Contains(chosen)) ProfilePanel.Selected = chosen;
    }

    // With no profile the box shows why, as its one (disabled) entry.
    private static readonly PrinterProfile[] NoProfiles = [new() { Name = "(no printer profiles)" }];

    private void ShowProfileList()
    {
        bool any = _profiles.Count > 0;
        if (!any)
        {
            ProfileBox.ItemsSource = NoProfiles;
            ProfileBox.SelectedIndex = 0;
        }
        else
        {
            if (!ReferenceEquals(ProfileBox.ItemsSource, _profiles)) ProfileBox.ItemsSource = _profiles;
            ProfileBox.Items.Refresh();
            if (!ReferenceEquals(ProfileBox.SelectedItem, CurrentProfile)) ProfileBox.SelectedItem = CurrentProfile;
        }
        ProfileBox.IsEnabled = any;
        ProfileBox.ToolTip = any ? "The printer to print to" : "Create a printer profile first.";
        ShowPrintState();
    }

    private void OnProfileChosen()
    {
        if (!ReferenceEquals(ProfileBox.SelectedItem, CurrentProfile)) ProfileBox.SelectedItem = CurrentProfile;
        if (CurrentProfile is { } profile && profile.Name != _config.App.SelectedProfile)
        {
            _config.App.SelectedProfile = profile.Name;
            SaveConfig();
            SetStatus($"Printer profile: {profile.Name}.");
        }
        // Each profile starts the job with its own number of copies.
        PrintCopiesBox.Text = (CurrentProfile?.Commands.Copies ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        OnProfileEdited();
    }

    // New values in the profile: a new 1-bit image of the same layout, and the Print panel.
    private void OnProfileEdited()
    {
        ShowImage();
        ShowPrintState();
    }

    /// <summary>The 1-bit image of the receipt laid out last, made with the chosen profile.</summary>
    private void ShowImage()
    {
        if (_rendered is null) return;
        if (CurrentProfile is { } p)
            Monochrome.SetSource(_rendered, RasterPlan.Create(p), p.PrintWidthDots);
        else
            Monochrome.SetSource(_rendered, null, 0);   // the control asks for a printer profile
    }

    // ---- the form and document chosen last ----------------------------------------

    // Inside the application's folder (the samples) the form is kept relative, so
    // the folder can be moved or copied as a whole.
    private static string? ToStoredPath(string? path)
    {
        if (path is null) return null;
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }

    private static string? FromStoredPath(string? stored)
        => string.IsNullOrWhiteSpace(stored) ? null : Path.GetFullPath(stored, AppContext.BaseDirectory);

    // Called when a round starts: the form and document it reads are the ones to come back to.
    private void RememberChoice(FormEntry form, DocumentEntry? document)
    {
        var stored = ToStoredPath(form.Path);
        var name = document?.Name ?? _config.App.LastDocument;
        if (stored == _config.App.LastForm && name == _config.App.LastDocument) return;
        _config.App.LastForm = stored;
        _config.App.LastDocument = name;
        SaveConfig();
    }

    /// <summary>The form chosen last, or the default sample when there is none (or it is gone).</summary>
    private string StartupForm()
        => FromStoredPath(_config.App.LastForm) is { } last && File.Exists(last) ? last
           : Path.Combine(SamplesFolder, DefaultForm + ".bpt");

    private string StartupDocument()
        => _config.App.LastDocument is { } last && SampleDocuments.All.Any(d => d.Name == last) ? last : DefaultDocument;

    // ---- window size and position, panels ------------------------------------------

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

    private void RestorePanels()
    {
        var app = _config.App;
        _leftWidth = Math.Max(app.LeftPanelWidth, LeftColumn.MinWidth);
        _rightWidth = Math.Max(app.RightPanelWidth, RightColumn.MinWidth);
        LeftColumn.Width = new GridLength(_leftWidth);
        RightColumn.Width = new GridLength(_rightWidth);
        LeftPanelMenuItem.IsChecked = app.LeftPanelVisible;
        RightPanelMenuItem.IsChecked = app.RightPanelVisible;
        PanelMenuItem_Click(this, new RoutedEventArgs());
    }

    private void RememberWindow()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var app = _config.App;
        app.Window = new WindowPlacement
        {
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        app.LeftPanelVisible = LeftPanelMenuItem.IsChecked;
        app.RightPanelVisible = RightPanelMenuItem.IsChecked;
        app.LeftPanelWidth = app.LeftPanelVisible ? LeftColumn.ActualWidth : _leftWidth;
        app.RightPanelWidth = app.RightPanelVisible ? RightColumn.ActualWidth : _rightWidth;
        app.Expanded = [.. ProfilePanel.ExpandedGroups];
        app.PreviewZoom = Monochrome.ZoomText;
    }
}
