// Printing: the Print panel below the printer profile, and the print job itself -
// every page or a range of them - sent over the profile's connection, or saved to
// a file.
//
// The job prints the receipt that is on screen: the layout made by the last
// Retrieve, turned into 1-bit images by the same RasterPlan as the preview, and
// wrapped in ESC/POS by BluePrint. The images are made one page at a time as the
// job goes, so a receipt of 235 pages does not sit in memory at once.

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using BluePrint.Render.Export.Mono;
using BluePrint.Render.Printing;
using BluePrint.UI.Controls;
using Microsoft.Win32;
using ReceiptPrinting.Printing;
using ReceiptPrinting.Profiles;
using ReceiptPrinting.Transports;

namespace ReceiptPrinting;

public partial class MainWindow
{
    private const string RangeHelp = "Use page numbers and ranges, for example 1-3,5.";

    private readonly PrintJobRunner _runner = new();
    private CancellationTokenSource? _jobCts;

    private bool IsPrinting => _jobCts is not null;

    // ---- the Print panel ----------------------------------------------------------

    // The profile in one line (where it prints to in the tooltip), what is worth
    // knowing before printing, and what Print waits for.
    private void ShowPrintState()
    {
        StatusProfileText.Text = CurrentProfile?.Name ?? "";
        if (CurrentProfile is not { } p)
        {
            PrintSummary.Text = "No printer profile";
            PrintSummary.ToolTip = null;
        }
        else
        {
            PrintSummary.Text = $"ESC/POS - {TransportCatalog.ShortNameOf(p.Transport.Kind)} - {p.Dpi} dpi - {p.PrintWidthDots} dots";
            var destination = TransportCatalog.Destination(p.Transport);
            PrintSummary.ToolTip = destination.Length > 0 ? destination : "No printer chosen yet";
        }

        var lines = new List<(BpMessageKind, string)>();
        if (SelectedDocument is null) lines.Add((BpMessageKind.Information, "Select a document to print."));
        if (CurrentProfile is { } profile && _renderedType is { } type)
            lines.AddRange(RasterChecks.Run(type, profile, _rendered, CopiesOrOne(), _tooLong));
        else if (CurrentProfile is { } any && _tooLong)
            lines.AddRange(RasterChecks.Run(BluePrint.Core.Models.ReportType.Canvas, any, null, 1, tooLong: true));
        PrintChecks.SetLines(lines);

        // A single page (every continuous receipt) has no range to choose.
        bool several = _rendered is { Pages.Count: > 1 };
        RangeRadio.IsEnabled = RangeBox.IsEnabled = several && !IsPrinting;
        RangeRadio.ToolTip = several ? RangeHelp : "This receipt has one page.";
        if (!several && RangeRadio.IsChecked == true) AllPagesRadio.IsChecked = true;
        RangeErrorIcon.Visibility = RangeIsValid() ? Visibility.Collapsed : Visibility.Visible;
        CopiesErrorIcon.Visibility = TryReadCopies(out _) ? Visibility.Collapsed : Visibility.Visible;

        PrintButton.ToolTip = WhyNotPrint() ?? "Print (Ctrl+P)";
        CommandManager.InvalidateRequerySuggested();
    }

    private void JobSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (RangeErrorIcon is null || CopiesErrorIcon is null) return;   // still loading the XAML
        ShowPrintState();
    }

    private bool RangeIsValid() => RangeRadio.IsChecked != true || BluePrintPageRange.TryParse(RangeBox.Text, out _);

    private BluePrintPageRange? SelectedRange()
        => RangeRadio.IsChecked == true && BluePrintPageRange.TryParse(RangeBox.Text, out var range) ? range : null;

    private bool TryReadCopies(out int copies)
        => int.TryParse(PrintCopiesBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out copies)
           && copies is >= 1 and <= 99;

    private int CopiesOrOne() => TryReadCopies(out var copies) ? copies : 1;

    /// <summary>Why Print cannot start now, for its tooltip; null when it can.</summary>
    private string? WhyNotPrint()
    {
        if (IsPrinting) return "A job is running.";
        if (SelectedForm is null) return "Select a form first.";
        if (CurrentProfile is not { } profile) return "Create a printer profile first.";
        if (ProfilePanel.HasProblems) return "Fix the printer profile first.";
        if (BusyPanel.Visibility == Visibility.Visible) return "The receipt is still being rendered.";
        if (_tooLong) return RasterChecks.TooLong;
        if (_rendered is null || _renderedType is not { } type) return "There is no receipt to print.";
        if (!RasterChecks.CanPrint(RasterChecks.Run(type, profile, _rendered, CopiesOrOne()))) return RasterChecks.NotAReceipt;
        if (!RangeIsValid()) return RangeHelp;
        if (!TryReadCopies(out _)) return "Enter a number of copies from 1 to 99.";
        return null;
    }

    // ---- commands ---------------------------------------------------------------

    // WPF asks already while InitializeComponent is still making the window - before
    // the lists that WhyNotPrint reads exist. It asks again once the window is loaded.
    private void CanPrint_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = IsLoaded && WhyNotPrint() is null;

    private void CanStop_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = IsPrinting;

    private void NotBusy_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !IsPrinting;

    private void Stop_Executed(object sender, ExecutedRoutedEventArgs e) => _jobCts?.Cancel();

    private async void Print_Executed(object sender, ExecutedRoutedEventArgs e) => await RunJobAsync(saveTo: null);

    private async void SaveCommands_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var name = SelectedDocument is { } document ? $"{SelectedForm?.Name} - {document.Name}" : SelectedForm?.Name ?? "receipt";
        var dialog = new SaveFileDialog
        {
            Title = "Save Commands",
            Filter = "ESC/POS commands (*.bin)|*.bin|All files (*.*)|*.*",
            FileName = string.Join("_", name.Split(Path.GetInvalidFileNameChars())) + ".bin",
        };
        if (dialog.ShowDialog(this) == true) await RunJobAsync(saveTo: dialog.FileName);
    }

    // A job is running: closing stops it, after asking.
    private bool ConfirmCloseWhilePrinting()
    {
        if (!IsPrinting) return true;
        if (BpMessage.Confirm(this, "A print job is running. Stop it and exit?", AppName, includeCancel: false) != BpConfirmResult.Yes)
            return false;
        _jobCts?.Cancel();
        return true;
    }

    // ---- the job -----------------------------------------------------------------

    /// <summary>Prints the chosen pages, or saves their commands to <paramref name="saveTo"/>.</summary>
    private async Task RunJobAsync(string? saveTo)
    {
        if (WhyNotPrint() is not null || CurrentProfile is not { } current || _rendered is not { } rendered) return;
        TryReadCopies(out var copies);

        // A copy of the profile for this job, with the copies typed in the panel:
        // the panel is locked while the job runs, and the file is not touched.
        var profile = AppConfigStore.Clone(current);
        profile.Commands.Copies = copies;

        var options = RasterPlan.Create(profile);
        options.PageRange = SelectedRange();
        MonochromePageCursor cursor;
        try
        {
            cursor = MonochromeRenderer.Open(rendered, options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            BpMessage.Show(this, $"Could not prepare the images: {ex.Message}", BpMessageKind.Error, title: "Print");
            return;
        }
        if (cursor.Count == 0)
        {
            cursor.Dispose();
            BpMessage.Show(this, "The pages chosen do not match any page of this receipt.", BpMessageKind.Warning, title: "Print");
            return;
        }

        var source = new CommandSource(profile.Commands);
        var count = cursor.Count;
        var jobName = $"{AppName} - {SelectedForm?.Name} - {SelectedDocument?.Name}";
        // Made here, on the UI thread, so its reports come back to the UI thread - not inside the
        // job, which runs on the thread pool.
        var progress = Reporter();
        try
        {
            if (saveTo is not null)
                await RunAsync(ct => SaveCommandsAsync(saveTo, Pages(cursor, source, count), count, progress, ct),
                    r => $"Saved {r.Bytes:N0} bytes of ESC/POS commands to {Path.GetFileName(saveTo)}.",
                    r => $"Last job: saved {r.Pages} page(s), {r.Bytes:N0} bytes.");
            else
                await RunAsync(ct => _runner.RunAsync(PrinterConnectionFactory.Create(profile.Transport, jobName),
                                                      Pages(cursor, source, count), count, profile.Pacing, progress, ct),
                    r => $"Printed {r.Pages} page(s), {r.Bytes:N0} bytes in {Seconds(r.Elapsed)} s.",
                    r => $"Last job: {r.Pages} page(s), {r.Bytes:N0} bytes in {Seconds(r.Elapsed)} s.");
        }
        finally
        {
            cursor.Dispose();
        }

        // Rendered lazily: the image of each page is made just before it is sent.
        static IEnumerable<CommandSegment> Pages(MonochromePageCursor cursor, CommandSource source, int count)
        {
            var i = 0;
            while (cursor.MoveNext()) yield return source.Page(cursor.Current, i++, count);
        }
    }

    private static string Seconds(TimeSpan elapsed) => elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

    // Save Commands: the same bytes as printing, into a file, without pauses.
    // Written to a .tmp file first, so a stopped job leaves no half file behind.
    private static async Task<PrintJobResult> SaveCommandsAsync(string path, IEnumerable<CommandSegment> pages, int count,
                                                                IProgress<PrintJobProgress> progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var temp = path + ".tmp";
        long bytes = 0;
        var done = 0;
        try
        {
            await using (var file = File.Create(temp))
            {
                foreach (var page in pages)
                {
                    ct.ThrowIfCancellationRequested();
                    await file.WriteAsync(page.Bytes, ct);
                    bytes += page.Bytes.Length;
                    progress.Report(new PrintJobProgress(++done, count, bytes, page.Bytes.Length, page.Bytes.Length));
                }
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            File.Delete(temp);
            throw new PrintJobStoppedException(bytes);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
        return new PrintJobResult(done, bytes, watch.Elapsed);
    }

    // Made on the UI thread, so its reports come back to the UI thread. The time
    // left comes from the bytes of the last 3 seconds; for a job of several pages
    // the pages still to come are taken to be as long as the ones sent.
    private IProgress<PrintJobProgress> Reporter()
    {
        var rate = new SendRate();
        var clock = Stopwatch.StartNew();
        return new Progress<PrintJobProgress>(p =>
        {
            rate.Add(clock.Elapsed, p.BytesSent);
            JobProgress.Value = p.Fraction;

            long jobBytes = p.PageCount <= 1 ? p.PageBytes
                : (long)Math.Round(p.BytesSent / Math.Max(p.Fraction, 1e-9));
            var left = rate.TimeLeft(jobBytes - p.BytesSent) is { } t ? $" - about {SendRate.Describe(t)} left" : "";
            var sending = $"Sending {SendRate.Bytes(p.PageBytesSent)} of {SendRate.Bytes(p.PageBytes)}{left}";
            JobProgressText.Text = p.PageCount > 1 ? $"Page {p.Page} of {p.PageCount} - {sending}" : sending;
        });
    }

    /// <summary>Runs a job in the background with Stop, progress and the result shown in the window.</summary>
    private async Task RunAsync(Func<CancellationToken, Task<PrintJobResult>> job,
                                Func<PrintJobResult, string> describe, Func<PrintJobResult, string> lastJob)
    {
        _jobCts = new CancellationTokenSource();
        var ct = _jobCts.Token;
        SetPrinting(true);
        try
        {
            var result = await Task.Run(() => job(ct), ct);
            SetStatus(describe(result), BpMessageKind.Success);
            LastJobLines.SetLines(BpMessageKind.Success, [lastJob(result)]);
        }
        catch (PrintJobStoppedException ex)
        {
            var text = $"Last job stopped after {ex.BytesSent:N0} bytes. The printer may still print the data it has received.";
            SetStatus(text, BpMessageKind.Warning);
            LastJobLines.SetLines(BpMessageKind.Warning, [text]);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Stopped before anything was sent.", BpMessageKind.Warning);
            LastJobLines.SetLines(BpMessageKind.Warning, ["Last job: Stopped before anything was sent."]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A connection that cannot be opened, a printer that stops answering, a
            // file that cannot be written: say so, and keep the window.
            SetStatus(ex.Message, BpMessageKind.Error);
            LastJobLines.SetLines(BpMessageKind.Error, [$"Last job failed: {ex.Message}"]);
            if (IsVisible) BpMessage.Show(this, ex.Message, BpMessageKind.Error, title: "Print");
        }
        finally
        {
            _jobCts.Dispose();
            _jobCts = null;
            SetPrinting(false);
        }
    }

    // While a job runs the receipt and the profile it was made from stay as they
    // are: what is on screen is what is being printed.
    private void SetPrinting(bool printing)
    {
        JobProgressPanel.Visibility = printing ? Visibility.Visible : Visibility.Collapsed;
        JobProgress.Value = 0;
        JobProgressText.Text = printing ? "Connecting..." : "";

        FormsList.IsEnabled = !printing;
        DocumentsGrid.IsEnabled = !printing;
        DocumentSearchBox.IsEnabled = !printing;
        ProfilePanel.IsEnabled = !printing;
        ProfileBox.IsEnabled = !printing && _profiles.Count > 0;
        JobSettings.IsEnabled = !printing;
        ShowPrintState();
    }
}
