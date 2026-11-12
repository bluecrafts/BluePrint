using System.Windows;
using System.Windows.Threading;
using BluePrint.UI.Controls;

namespace SharingHtmlReports;

public partial class App : Application
{
    private const string AppName = "Sharing HTML Reports via Email";

    // One report at a time is enough: a second exception raised while the first
    // message is still open would stack dialogs on top of each other.
    private bool _reporting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Without these, an exception that escapes an event handler closes the
        // application on the spot, with no message.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        base.OnStartup(e);
    }

    // On the UI thread: report it and keep running.
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Report(e.Exception, stillRunning: true);
    }

    // A task nobody awaited failed. Mark it observed so it cannot end the process later.
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Dispatcher.BeginInvoke(() => Report(e.Exception.GetBaseException(), stillRunning: true));
    }

    // On another thread the process cannot be saved; at least say why it ends.
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Dispatcher.Invoke(() => Report(ex, stillRunning: false));
    }

    private void Report(Exception ex, bool stillRunning)
    {
        if (_reporting) return;
        _reporting = true;

        // An exception while the main window is still being built: there is no window
        // to go back to, so the application has to close.
        var owner = MainWindow is { IsLoaded: true } window ? window : null;
        if (owner is null) stillRunning = false;
        try
        {
            // Nothing to save here: the reports are only read, and the settings are
            // written as soon as they change.
            var what = stillRunning
                ? "Something went wrong, but the application is still running."
                : "Something went wrong and the application has to close.";
            var next = stillRunning
                ? "Try again. If it happens again, restart the application."
                : "Restart the application.";
            BpMessage.ShowDetails(owner,
                $"{what}\n\n{ex.GetType().Name}: {ex.Message}\n\n{next}",
                ex.ToString(), BpMessageKind.Error, title: AppName);
        }
        catch
        {
            // The message itself failed; there is nothing left to show it with.
        }
        finally
        {
            _reporting = false;
        }
        if (owner is null) Shutdown(1);
    }
}
