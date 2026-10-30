using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BluePrint.UI.Controls;

namespace LabelPrinting;

public partial class App : Application
{
    // One report at a time is enough: a second exception raised while the first
    // message is still open would stack dialogs on top of each other.
    private bool _reporting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Without these, an exception that escapes an event handler closes the
        // application on the spot, with no message and with the label lost.
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
        try
        {
            var what = stillRunning
                ? "Something went wrong, but LabelPrinting is still running."
                : "Something went wrong and LabelPrinting has to close.";
            var next = stillRunning
                ? "Save your work, then try again. If it happens again, restart the application."
                : "Changes since the last save are lost.";

            BpMessage.ShowDetails(MainWindow,
                $"{what}\n\n{ex.GetType().Name}: {ex.Message}\n\n{next}",
                ex.ToString(), BpMessageKind.Error, title: "LabelPrinting");
        }
        catch
        {
            // The message itself failed; there is nothing left to show it with.
        }
        finally
        {
            _reporting = false;
        }
    }
}
