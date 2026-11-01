using System.Windows.Input;

namespace ReceiptPrinting;

/// <summary>
/// Commands of this application that WPF has no built-in command for. Open Form
/// uses <see cref="ApplicationCommands.Open"/> (Ctrl+O).
/// </summary>
public static class AppCommands
{
    public static readonly RoutedUICommand Retrieve = new(
        "Retrieve", nameof(Retrieve), typeof(AppCommands),
        [new KeyGesture(Key.R, ModifierKeys.Control)]);

    public static readonly RoutedUICommand Print = new(
        "Print", nameof(Print), typeof(AppCommands),
        [new KeyGesture(Key.P, ModifierKeys.Control)]);

    public static readonly RoutedUICommand SaveCommands = new(
        "Save Commands", nameof(SaveCommands), typeof(AppCommands),
        [new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift)]);

    // Esc stops a job; when nothing is printing the command is disabled, so Esc
    // is left to whatever else wants it.
    public static readonly RoutedUICommand Stop = new(
        "Stop", nameof(Stop), typeof(AppCommands),
        [new KeyGesture(Key.Escape)]);

    // Zoom of the 1-bit preview. OemPlus is the "=" key; the numeric keypad works too.
    public static readonly RoutedUICommand ZoomIn = new(
        "Zoom In", nameof(ZoomIn), typeof(AppCommands),
        [new KeyGesture(Key.OemPlus, ModifierKeys.Control, "Ctrl+="), new KeyGesture(Key.Add, ModifierKeys.Control)]);

    public static readonly RoutedUICommand ZoomOut = new(
        "Zoom Out", nameof(ZoomOut), typeof(AppCommands),
        [new KeyGesture(Key.OemMinus, ModifierKeys.Control, "Ctrl+-"), new KeyGesture(Key.Subtract, ModifierKeys.Control)]);
}
