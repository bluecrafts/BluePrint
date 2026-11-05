// Localization.cfg in the window: the form, document and language chosen last,
// the theme, the size and position of the window and the width of the left panel.
// Written when a choice changes and when the window closes.

using System.IO;
using System.Windows;
using BluePrint.UI.Controls;
using LocalizationShowcase.Forms;

namespace LocalizationShowcase;

public partial class MainWindow
{
    private readonly AppConfigStore _store = AppConfigStore.Default;
    private AppConfig _config = new();

    // A failed save is reported once, not again for every choice after it, until a save works.
    private bool _saveErrorShown;

    private void LoadConfig()
    {
        _config = _store.Load();
        RestoreWindowPlacement(_config.Window);
        _leftWidth = Math.Max(_config.LeftPanelWidth, LeftColumn.MinWidth);
        LeftColumn.Width = new GridLength(_leftWidth);
        LeftPanelMenuItem.IsChecked = _config.LeftPanelVisible;
        _rightWidth = Math.Max(_config.RightPanelWidth, RightColumn.MinWidth);
        RightColumn.Width = new GridLength(_rightWidth);
        RightPanelMenuItem.IsChecked = _config.RightPanelVisible;
        ShowLeftPanel(_config.LeftPanelVisible);
        ShowRightPanel(_config.RightPanelVisible);
        SourcesExpander.IsExpanded = _config.SourcesExpanded;
    }

    private void SaveConfig()
    {
        try
        {
            _store.Save(_config);
            _saveErrorShown = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_saveErrorShown || !IsLoaded) return;
            _saveErrorShown = true;
            BpMessage.Show(this,
                $"Could not save {AppConfigStore.FileName} in {_store.Folder}. " +
                $"Run the application from a folder you can write to.\n\n{ex.Message}",
                BpMessageKind.Error, title: AppName);
        }
    }

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
    private void RememberChoice(FormEntry form, DocumentEntry document)
    {
        var stored = ToStoredPath(form.Path);
        if (stored == _config.LastForm && document.Name == _config.LastDocument) return;
        _config.LastForm = stored;
        _config.LastDocument = document.Name;
        SaveConfig();
    }

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

    private void RememberWindow()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _config.Window = new WindowPlacement
        {
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        _config.LeftPanelVisible = LeftPanelMenuItem.IsChecked;
        _config.LeftPanelWidth = _config.LeftPanelVisible ? LeftColumn.ActualWidth : _leftWidth;
        _config.RightPanelVisible = RightPanelMenuItem.IsChecked;
        _config.RightPanelWidth = _config.RightPanelVisible ? RightColumn.ActualWidth : _rightWidth;
    }
}
