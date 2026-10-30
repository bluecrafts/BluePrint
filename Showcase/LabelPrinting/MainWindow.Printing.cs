// Printing: the Print panel of the Preview tab, the 1-bit image beside it,
// and the print job itself - all rows, a range, or row by row -
// sent over the printer profile's connection, or saved to a file.
//
// The pages are rendered from the label as it is in the designer right now
// (unsaved changes included) with the rows retrieved last; BluePrint turns
// each page into a 1-bit image at the printer's resolution and wraps it in the
// printer's command language. A page of a roll label report is one row of
// labels.

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BluePrint.Render;
using BluePrint.Render.Export.Mono;
using BluePrint.Render.Models;
using BluePrint.Render.Printing;
using BluePrint.Render.WPF.Controls;
using BluePrint.UI.Controls;
using LabelPrinting.Printing;
using LabelPrinting.Profiles;
using LabelPrinting.Transports;
using Microsoft.Win32;

namespace LabelPrinting;

public partial class MainWindow
{
    private readonly PrintJobRunner _runner = new();
    private readonly DispatcherTimer _rowImageTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? _jobCts;
    private ImageSize? _rowImageSize;

    // Row by row: the pages stay rendered between rows, so each row is sent
    // from the same layout and only the first one initializes the printer.
    private MonochromePageCursor? _rowCursor;
    private PrinterProfile? _rowProfile;
    private int _rowPosition = -1;

    private bool IsPrinting => _jobCts is not null;

    private void InitializePrinting()
    {
        _rowImageTimer.Tick += (_, _) =>
        {
            _rowImageTimer.Stop();
            RefreshMonochrome();
        };

        // The image follows the label as it is being designed.
        Designer.DesignChanged += () =>
        {
            ResetRows();
            ScheduleRowImage();
        };

        // The warnings in the Print panel compare the image with the profile.
        Monochrome.ImageRendered += (_, bitmap) =>
        {
            _rowImageSize = bitmap is { } b ? new ImageSize(b.Width, b.Height, b.Dpi) : null;
            UpdatePrintHints();
        };

        UpdatePrintState();
    }

    /// <summary>Something the row image depends on changed: render it again shortly.</summary>
    private void ScheduleRowImage()
    {
        _rowImageTimer.Stop();
        _rowImageTimer.Start();
    }

    // ---- the Print panel -----------------------------------------------------

    // The profile in one line (what it prints to in the tooltip), and what is
    // worth knowing before printing.
    private void UpdatePrintHints()
    {
        var profile = CurrentProfile;
        StatusProfileText.Text = profile?.Name ?? "";
        ChangeProfileLink.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
        if (profile is null)
        {
            ProfileSummary.Text = _config.PrinterProfiles.Count == 0 ? "No printer profile" : "";
            ProfileSummary.ToolTip = null;
            PrintHints.SetLines(BpMessageKind.Information, _config.PrinterProfiles.Count == 0
                ? ["No printer profiles yet. Choose Print > Printer Profiles to create one from a preset."]
                : []);
            return;
        }

        ProfileSummary.Text = $"{PrinterCommandCatalog.Of(profile.PrinterCommand).DisplayName} - " +
                              $"{TransportCatalog.NameOf(profile.Transport.Kind)} - {profile.Dpi} dpi - {profile.PrintWidthDots} dots";
        // The same words as under the 1-bit image (BluePrint.Render.WPF), so the two read alike.
        if (MonoTransformText.Describe(profile.Raster.Orientation, profile.Raster.Mirror, profile.Raster.Negative) is { } transform)
            ProfileSummary.Text += $" - {transform}";
        ProfileSummary.ToolTip = $"{Destination(profile.Transport)} - row {Number(profile.Label.WidthMm / 10)} x {Number(profile.Label.HeightMm / 10)} cm";

        var lines = new List<(BpMessageKind, string)>();
        if (Designer.ReportDocument is { } doc)
        {
            if (doc.DataProvider is null)
                lines.Add((BpMessageKind.Warning, "No data retrieved. Labels will print without data."));
            lines.AddRange(RasterChecks.Run(doc.Definition, profile, _rowImageSize));
        }
        PrintHints.SetLines(lines);
    }

    private static string Destination(TransportSettings t) => t.Kind switch
    {
        TransportKind.Tcp => t.Tcp.Host.Length > 0 ? $"{t.Tcp.Host}:{t.Tcp.Port}" : "No printer address yet",
        TransportKind.Usb => t.Usb.DeviceName.Length > 0 ? t.Usb.DeviceName : "No USB printer chosen yet",
        TransportKind.SpoolerRaw => t.SpoolerRaw.QueueName.Length > 0 ? $"Queue {t.SpoolerRaw.QueueName}" : "No print queue chosen yet",
        TransportKind.Serial => t.Serial.PortName.Length > 0 ? t.Serial.PortName : "No COM port chosen yet",
        _ => "",
    };

    // ---- the 1-bit image (Preview tab) ------------------------------------------------------

    /// <summary>
    /// Gives the 1-bit image of the Preview tab the label as it is now. BluePrintMonochromeView
    /// (BluePrint.Render.WPF) makes the image only while it is on screen, so a change made in the
    /// Design tab is drawn when the Preview tab is shown.
    /// </summary>
    private void RefreshMonochrome()
    {
        UpdatePrintHints();

        if (Designer.ReportDocument is not { } doc)
        {
            Monochrome.ShowMessage("Open a label to start. File > Open, or Ctrl+O.");
            return;
        }
        if (CurrentProfile is not { } profile)
        {
            Monochrome.ShowMessage("Select a printer profile to see the image that will be sent.");
            return;
        }
        if (!RasterChecks.IsLabelReport(doc.Definition))
        {
            Monochrome.ShowMessage("This is not a label report. LabelPrinting prints label reports only.");
            return;
        }
        // BluePrint would refuse to make the image; say why instead.
        if (RasterChecks.Run(doc.Definition, profile, null).FirstOrDefault(r => r.Kind == BpMessageKind.Error) is { Text: { } refused })
        {
            Monochrome.ShowMessage(refused);
            return;
        }

        var rendered = RenderNow(doc, out var error);
        if (rendered is null)
        {
            Monochrome.ShowMessage(error ?? "Could not lay out the label.");
            return;
        }
        Monochrome.SetSource(rendered, RasterPlan.Create(profile), profile.PrintWidthDots);
    }

    /// <summary>
    /// The label as it is now, laid out with the rows retrieved last. On the UI
    /// thread: the designer may change the label at any moment, and the layout of
    /// a label is quick. The result is immutable, so it can then be used anywhere.
    /// </summary>
    private RenderedDocument? RenderNow(BluePrintDocument doc, out string? error)
    {
        error = null;
        try
        {
            return doc.Render();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = $"Could not lay out the label: {ex.Message}";
            return null;
        }
    }

    // ---- the job settings ------------------------------------------------------

    private void Range_Changed(object sender, RoutedEventArgs e)
    {
        if (RangeErrorIcon is null) return;   // still loading the XAML
        RangeErrorIcon.Visibility = RangeIsValid() ? Visibility.Collapsed : Visibility.Visible;
        CommandManager.InvalidateRequerySuggested();
    }

    private bool RangeIsValid() => RangeRadio.IsChecked != true || BluePrintPageRange.TryParse(RangeBox.Text, out _);

    private BluePrintPageRange? SelectedRange()
        => RangeRadio.IsChecked == true && BluePrintPageRange.TryParse(RangeBox.Text, out var range) ? range : null;

    private bool TryReadCopies(out int copies)
        => int.TryParse(CopiesBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out copies)
           && copies is >= 1 and <= 999;

    // A copy of the profile for one job, so a change made while it runs does not
    // touch it, with the copies typed in the panel.
    private PrinterProfile? JobProfile()
    {
        if (CurrentProfile is not { } current || !TryReadCopies(out var copies)) return null;
        var profile = AppConfigStore.Clone(current);
        profile.Commands.Copies = copies;
        return profile;
    }

    private string JobName() => $"LabelPrinting - {Path.GetFileName(Designer.CurrentFilePath) ?? Designer.Title}";

    // ---- commands ----------------------------------------------------------------

    private bool ReadyToPrint =>
        Designer is { Report: { } report, IsRetrieving: false }
        && CurrentProfile is { } profile && RasterChecks.CanPrint(report, profile) && !IsPrinting
        && TryReadCopies(out _);

    private void CanPrint_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = ReadyToPrint && RangeIsValid();

    private void CanPrintNextRow_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = ReadyToPrint && (_rowCursor is null || _rowPosition + 1 < _rowCursor.Count);

    private void CanStop_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = IsPrinting;

    private void Stop_Executed(object sender, ExecutedRoutedEventArgs e) => _jobCts?.Cancel();

    private async void Print_Executed(object sender, ExecutedRoutedEventArgs e) => await RunJobAsync(saveTo: null);

    private async void SaveCommands_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (CurrentProfile is not { } profile) return;
        var entry = PrinterCommandCatalog.Of(profile.PrinterCommand);
        var dialog = new SaveFileDialog
        {
            Title = "Save Commands",
            Filter = $"{entry.DisplayName} commands (*{entry.FileExtension})|*{entry.FileExtension}|All files (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(Designer.CurrentFilePath ?? Designer.Title) + entry.FileExtension,
            InitialDirectory = StartFolder(),
        };
        if (dialog.ShowDialog(this) == true) await RunJobAsync(saveTo: dialog.FileName);
    }

    // ---- the job -------------------------------------------------------------

    /// <summary>Prints the chosen rows, or saves their commands to <paramref name="saveTo"/>.</summary>
    private async Task RunJobAsync(string? saveTo)
    {
        if (JobProfile() is not { } profile || Designer.ReportDocument is not { } doc) return;

        var rendered = RenderNow(doc, out var error);
        if (rendered is null)
        {
            BpMessage.Show(this, error ?? "Could not lay out the label.", BpMessageKind.Error, title: "Print");
            return;
        }

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
            BpMessage.Show(this, rendered.Pages.Count == 0
                    ? "Nothing to print. Retrieve the label data first."
                    : "The rows chosen do not match any row.",
                BpMessageKind.Warning, title: "Print");
            return;
        }

        var source = new CommandSource(profile.PrinterCommand, profile.Commands);
        var count = cursor.Count;
        var language = PrinterCommandCatalog.Of(profile.PrinterCommand).DisplayName;
        var jobName = JobName();
        // Made here, on the UI thread, so its reports come back to the UI thread.
        var progress = JobProgressReporter();
        try
        {
            if (saveTo is not null)
                await RunAsync(ct => SaveCommandsAsync(saveTo, Pages(cursor, source, count), count, progress, ct), count,
                    r => $"Saved {r.Bytes:N0} bytes of {language} commands to {Path.GetFileName(saveTo)}.");
            else
                await RunAsync(ct => _runner.RunAsync(PrinterConnectionFactory.Create(profile.Transport, jobName),
                                                      Pages(cursor, source, count), count, profile.Pacing, progress, ct),
                    count, r => $"Printed {r.Pages} row(s), {r.Bytes:N0} bytes in {r.Elapsed.TotalSeconds:0.0} s.");
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

    // Save Commands: the same bytes as printing, into a file, without pauses.
    // Written to a .tmp file first, so a stopped job leaves no half file behind.
    private static async Task<PrintJobResult> SaveCommandsAsync(string path, IEnumerable<CommandSegment> pages, int count,
                                                                IProgress<PrintJobProgress> progress, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
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
                    progress.Report(new PrintJobProgress(++done, count, bytes));
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

    private IProgress<PrintJobProgress> JobProgressReporter() => new Progress<PrintJobProgress>(p =>
    {
        JobProgress.Maximum = p.PageCount;
        JobProgress.Value = p.Page;
        JobProgressText.Text = $"Printing row {p.Page} of {p.PageCount}";
    });

    /// <summary>
    /// Runs a job in the background with Stop, progress and the result shown in the
    /// window. Returns whether it finished.
    /// </summary>
    private async Task<bool> RunAsync(Func<CancellationToken, Task<PrintJobResult>> job, int count,
                                      Func<PrintJobResult, string> describe)
    {
        _jobCts = new CancellationTokenSource();
        var ct = _jobCts.Token;
        SetPrinting(true, count);
        try
        {
            var result = await Task.Run(() => job(ct), ct);
            var text = describe(result);
            SetStatus(text, BpMessageKind.Success);
            LastJobLines.SetLines(BpMessageKind.Success, [$"Last job: {text}"]);
            return true;
        }
        catch (PrintJobStoppedException ex)
        {
            SetStatus(ex.Message, BpMessageKind.Warning);
            LastJobLines.SetLines(BpMessageKind.Warning, [$"Last job: {ex.Message}"]);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Stopped before anything was sent.", BpMessageKind.Warning);
            LastJobLines.SetLines(BpMessageKind.Warning, ["Last job: Stopped before anything was sent."]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A connection that cannot be opened, a printer that stops answering,
            // a command option the encoder refuses: say so, and keep the window.
            SetStatus(ex.Message, BpMessageKind.Error);
            LastJobLines.SetLines(BpMessageKind.Error, [$"Last job failed: {ex.Message}"]);
            BpMessage.Show(this, ex.Message, BpMessageKind.Error, title: "Print");
        }
        finally
        {
            _jobCts.Dispose();
            _jobCts = null;
            SetPrinting(false, 0);
        }
        return false;
    }

    private void SetPrinting(bool printing, int count)
    {
        JobProgressItem.Visibility = printing ? Visibility.Visible : Visibility.Collapsed;
        JobProgress.Maximum = Math.Max(1, count);
        JobProgress.Value = 0;
        JobProgressText.Text = printing ? $"Printing row 0 of {count}" : "";

        // The job uses its own copy of the profile; changing it now would only
        // suggest otherwise.
        ProfileBox.IsEnabled = !printing && _config.PrinterProfiles.Count > 0;
        UpdatePrintState();
    }

    // ---- row by row ------------------------------------------------------------

    private async void PrintNextRow_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Designer.ReportDocument is not { } doc) return;

        if (_rowCursor is null)
        {
            if (JobProfile() is not { } profile) return;
            var rendered = RenderNow(doc, out var error);
            if (rendered is null || rendered.Pages.Count == 0)
            {
                BpMessage.Show(this, error ?? "Nothing to print. Retrieve the label data first.",
                    BpMessageKind.Warning, title: "Print");
                return;
            }
            try
            {
                _rowCursor = MonochromeRenderer.Open(rendered, RasterPlan.Create(profile));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                BpMessage.Show(this, $"Could not prepare the images: {ex.Message}", BpMessageKind.Error, title: "Print");
                return;
            }
            _rowProfile = profile;
        }

        var cursor = _rowCursor;
        var rowProfile = _rowProfile!;
        var index = _rowPosition + 1;
        var source = new CommandSource(rowProfile.PrinterCommand, rowProfile.Commands);
        await SendRowAsync(rowProfile, () => { cursor.MoveNext(); return source.Page(cursor.Current, index, cursor.Count); },
                           $"row {index + 1} of {cursor.Count}");
        if (cursor.Position == index) _rowPosition = index;   // not moved when the job failed before the row was made
        UpdatePrintState();
    }

    private async void Reprint_Click(object sender, RoutedEventArgs e)
    {
        if (_rowCursor is not { } cursor || _rowProfile is not { } profile || _rowPosition < 0) return;

        var index = _rowPosition;
        var source = new CommandSource(profile.PrinterCommand, profile.Commands);
        await SendRowAsync(profile, () => source.Page(cursor.Get(index), index, cursor.Count, allowInitialize: false),
                           $"row {index + 1} of {cursor.Count} again");
    }

    private void ResetRows_Click(object sender, RoutedEventArgs e)
    {
        ResetRows();
        SetStatus("Row by row starts again from the first row.");
    }

    private Task<bool> SendRowAsync(PrinterProfile profile, Func<CommandSegment> makeRow, string what)
    {
        var jobName = JobName();
        return RunAsync(ct => _runner.RunAsync(PrinterConnectionFactory.Create(profile.Transport, jobName), Once(makeRow), 1,
                                               profile.Pacing, null, ct),
                        1, r => $"Printed {what}, {r.Bytes:N0} bytes.");
    }

    private static IEnumerable<CommandSegment> Once(Func<CommandSegment> make)
    {
        yield return make();
    }

    /// <summary>The label, its rows or the profile changed: row by row starts from the first row again.</summary>
    private void ResetRows()
    {
        if (IsPrinting) return;
        _rowCursor?.Dispose();
        _rowCursor = null;
        _rowProfile = null;
        _rowPosition = -1;
        UpdatePrintState();
    }

    private void UpdatePrintState()
    {
        if (_rowCursor is null)
            NextRowText.Text = "Next: row 1";
        else if (_rowPosition + 1 >= _rowCursor.Count)
            NextRowText.Text = "All rows printed.";
        else
            NextRowText.Text = $"Next: row {_rowPosition + 2} of {_rowCursor.Count}";

        ReprintButton.IsEnabled = !IsPrinting && _rowCursor is not null && _rowPosition >= 0;
        ResetRowsButton.IsEnabled = !IsPrinting && _rowCursor is not null;
        CommandManager.InvalidateRequerySuggested();
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
