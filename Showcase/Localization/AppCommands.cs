using System.Windows.Input;

namespace LocalizationShowcase;

/// <summary>
/// Commands of this application that WPF has no built-in command for. Open Form
/// uses <see cref="ApplicationCommands.Open"/> (Ctrl+O) and the document search
/// <see cref="ApplicationCommands.Find"/> (Ctrl+F).
/// </summary>
public static class AppCommands
{
    public static readonly RoutedUICommand ReloadForm = new(
        "Reload Form", nameof(ReloadForm), typeof(AppCommands),
        [new KeyGesture(Key.F5)]);

    public static readonly RoutedUICommand Retrieve = new(
        "Retrieve", nameof(Retrieve), typeof(AppCommands),
        [new KeyGesture(Key.R, ModifierKeys.Control)]);

    // Ctrl+[ and Ctrl+]: compare two languages quickly.
    public static readonly RoutedUICommand PreviousLanguage = new(
        "Previous Language", nameof(PreviousLanguage), typeof(AppCommands),
        [new KeyGesture(Key.OemOpenBrackets, ModifierKeys.Control, "Ctrl+[")]);

    public static readonly RoutedUICommand NextLanguage = new(
        "Next Language", nameof(NextLanguage), typeof(AppCommands),
        [new KeyGesture(Key.OemCloseBrackets, ModifierKeys.Control, "Ctrl+]")]);

    // Ctrl+1 ... Ctrl+7: the languages in the order of the list. The parameter is the position.
    public static readonly RoutedUICommand SelectLanguage = new("Select Language", nameof(SelectLanguage), typeof(AppCommands));
}
