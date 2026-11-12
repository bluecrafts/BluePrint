using System.Windows.Input;

namespace SharingHtmlReports;

/// <summary>Commands of this application, with their keys.</summary>
public static class AppCommands
{
    public static readonly RoutedUICommand Retrieve = new(
        "Retrieve", nameof(Retrieve), typeof(AppCommands),
        [new KeyGesture(Key.R, ModifierKeys.Control)]);

    public static readonly RoutedUICommand Send = new(
        "Send", nameof(Send), typeof(AppCommands),
        [new KeyGesture(Key.Enter, ModifierKeys.Control, "Ctrl+Enter")]);

    public static readonly RoutedUICommand SaveEml = new(
        "Save .eml", nameof(SaveEml), typeof(AppCommands),
        [new KeyGesture(Key.S, ModifierKeys.Control)]);

    public static readonly RoutedUICommand OpenInMailApp = new(
        "Open in Mail App", nameof(OpenInMailApp), typeof(AppCommands),
        [new KeyGesture(Key.O, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+O")]);

    public static readonly RoutedUICommand OpenInBrowser = new(
        "Open in Browser", nameof(OpenInBrowser), typeof(AppCommands),
        [new KeyGesture(Key.B, ModifierKeys.Control)]);

    public static readonly RoutedUICommand ShowReportTab = new(
        "Report", nameof(ShowReportTab), typeof(AppCommands),
        [new KeyGesture(Key.D1, ModifierKeys.Control, "Ctrl+1")]);

    public static readonly RoutedUICommand ShowEmailTab = new(
        "Email", nameof(ShowEmailTab), typeof(AppCommands),
        [new KeyGesture(Key.D2, ModifierKeys.Control, "Ctrl+2")]);
}
