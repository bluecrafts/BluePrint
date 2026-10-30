using System.Windows.Input;

namespace LabelPrinting;

/// <summary>
/// Commands of this application that WPF has no built-in command for. Open,
/// Save and Save As use <see cref="ApplicationCommands"/>.
/// </summary>
public static class AppCommands
{
    // Not ApplicationCommands.New: its Ctrl+N is Print Next Row here, so New has no gesture.
    public static readonly RoutedUICommand New = new("New", nameof(New), typeof(AppCommands));

    // Ctrl+R, not F5: the designer already uses F5 to render its preview again.
    public static readonly RoutedUICommand Retrieve = new(
        "Retrieve", nameof(Retrieve), typeof(AppCommands),
        [new KeyGesture(Key.R, ModifierKeys.Control)]);

    public static readonly RoutedUICommand Print = new(
        "Print", nameof(Print), typeof(AppCommands),
        [new KeyGesture(Key.P, ModifierKeys.Control)]);

    public static readonly RoutedUICommand PrintNextRow = new(
        "Print Next Row", nameof(PrintNextRow), typeof(AppCommands),
        [new KeyGesture(Key.N, ModifierKeys.Control)]);

    public static readonly RoutedUICommand SaveCommands = new(
        "Save Commands", nameof(SaveCommands), typeof(AppCommands),
        [new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift)]);

    // Esc stops a job; when nothing is printing the command is disabled, so Esc
    // is left to whatever else wants it.
    public static readonly RoutedUICommand Stop = new(
        "Stop", nameof(Stop), typeof(AppCommands),
        [new KeyGesture(Key.Escape)]);

    public static readonly RoutedUICommand ShowDesign = new(
        "Design", nameof(ShowDesign), typeof(AppCommands),
        [new KeyGesture(Key.D1, ModifierKeys.Control)]);

    public static readonly RoutedUICommand ShowPreview = new(
        "Preview", nameof(ShowPreview), typeof(AppCommands),
        [new KeyGesture(Key.D2, ModifierKeys.Control)]);

    // Zoom of the tab on screen. OemPlus is the "=" key; the numeric keypad works too.
    public static readonly RoutedUICommand ZoomIn = new(
        "Zoom In", nameof(ZoomIn), typeof(AppCommands),
        [new KeyGesture(Key.OemPlus, ModifierKeys.Control, "Ctrl+="), new KeyGesture(Key.Add, ModifierKeys.Control)]);

    public static readonly RoutedUICommand ZoomOut = new(
        "Zoom Out", nameof(ZoomOut), typeof(AppCommands),
        [new KeyGesture(Key.OemMinus, ModifierKeys.Control, "Ctrl+-"), new KeyGesture(Key.Subtract, ModifierKeys.Control)]);
}
