// SharingHTMLReportsViaEmail.cfg in the window: written when a choice changes, when
// a field loses the focus and when the window closes.

using System.IO;
using System.Windows;
using BluePrint.UI.Controls;

namespace SharingHtmlReports;

public partial class MainWindow
{
    private readonly AppConfigStore _store = AppConfigStore.Default;
    private AppConfig _config = new();

    // A failed save is reported once, not again for every change after it, until a save works.
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

    private void RestoreWindowPlacement(WindowPlacement? placement)
    {
        if (placement is not { Width: > 0, Height: > 0 }) return;

        // A position on a screen that is no longer there would put the window out of reach.
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
