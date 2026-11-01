// Retrieve -> render -> 1-bit image: what happens after a form or a document is
// chosen. Retrieve reads the document with the form's own SQL; render lays the
// receipt out; the preview turns the page into the dots the printer will get.
//
// Choosing quickly - or a long document still rendering - starts a new round
// that cancels the one before, and a round that finishes late never replaces a
// newer result.

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BluePrint.Core.Models;
using BluePrint.Render;
using BluePrint.Render.Models;
using BluePrint.UI.Controls;
using Microsoft.Data.Sqlite;
using ReceiptPrinting.Printing;

namespace ReceiptPrinting;

public partial class MainWindow
{
    // Text of the exception BluePrint throws for a continuous page taller than its 10 m limit.
    private const string TooLongPrefix = "Continuous page exceeds the maximum height";
    private const string TooLongMessage = RasterChecks.TooLong;

    private readonly DispatcherTimer _retrieveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? _roundCts;
    private int _round;

    // The form that is open: read when it is chosen, so the title and the arguments come from it.
    private BluePrintDocument? _openForm;

    // The receipt laid out last. Another printer profile makes a new 1-bit image of
    // it, without reading or laying it out again.
    private RenderedDocument? _rendered;

    // What the Print panel checks besides the layout: the kind of report, and a
    // continuous receipt that was too long to lay out at all.
    private ReportType? _renderedType;
    private bool _tooLong;

    private void InitializePreview()
    {
        _retrieveTimer.Tick += (_, _) =>
        {
            _retrieveTimer.Stop();
            _ = RunRoundAsync();
        };
        Monochrome.ShowMessage("Select a form on the left.");
    }

    private bool TryOpenForm(string path, out string error)
    {
        try
        {
            _openForm = BluePrintDocument.Load(path);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = ex.Message;
            return false;
        }
    }

    // A form or a document was chosen: wait for the choice to settle (arrow keys through the list).
    private void ScheduleRetrieve()
    {
        _retrieveTimer.Stop();
        _retrieveTimer.Start();
        UpdateTitle(SelectedForm?.Name, SelectedDocument?.Name);
    }

    private void RetrieveNow()
    {
        _retrieveTimer.Stop();
        _ = RunRoundAsync();
    }

    private void Retrieve_Executed(object sender, ExecutedRoutedEventArgs e) => RetrieveNow();

    private void CanRetrieve_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _openForm is not null && SelectedDocument is not null && DatabaseExists && !IsPrinting;

    private async Task RunRoundAsync()
    {
        _roundCts?.Cancel();
        var cts = _roundCts = new CancellationTokenSource();
        int round = ++_round;
        var ct = cts.Token;

        var form = SelectedForm;
        var document = SelectedDocument;
        if (form is null || _openForm is null)
        {
            ShowNoImage("Select a form on the left.");
            return;
        }

        RememberChoice(form, document);
        ShowBusy($"Rendering {document?.Name ?? form.Name}...");
        try
        {
            // A fresh copy for every round: two rounds never share the rows of one report.
            var doc = BluePrintDocument.Load(form.Path);
            var definition = doc.Definition;
            bool reads = definition.DataSource?.DataSourceType == DataSourceType.DBSQL
                         && !string.IsNullOrWhiteSpace(definition.DataSource.Sql);

            if (reads && document is not null && DatabaseExists)
            {
                try
                {
                    await using var conn = new SqliteConnection(SampleDocuments.ConnectionString());
                    await doc.RetrieveAsync(conn, SampleDocuments.ArgumentsFor(document.Document, definition), ct: ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    if (round != _round) return;
                    SetStatus($"Retrieve failed: {ex.Message}", BpMessageKind.Error);
                    ShowNoImage($"Retrieve failed: {ex.Message}");
                    return;
                }
            }

            var rendered = await Task.Run(() => doc.Render(ct: ct), ct);
            if (round != _round) return;

            if (rendered is null)
            {
                ShowNoImage("The report has no pages.");
                return;
            }

            _rendered = rendered;
            _renderedType = definition.Report.Type;
            ShowImage();
            PaperText.Text = PaperLength(rendered);

            if (definition.Report.Type != ReportType.Canvas)
                SetStatus("This is not a receipt report. ReceiptPrinting prints canvas reports on roll paper.", BpMessageKind.Error);
            else if (!reads)
                SetStatus("This form does not read from a database.");
            else if (document is not null && DatabaseExists)
                SetStatus($"Retrieved {rendered.Diagnostics?.RowCount.ToString("#,##0", CultureInfo.InvariantCulture)} lines for {document.Name}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A newer round took over.
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith(TooLongPrefix, StringComparison.Ordinal))
        {
            if (round != _round) return;
            ShowNoImage(TooLongMessage);
            _tooLong = true;
            SetStatus(TooLongMessage, BpMessageKind.Error);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (round != _round) return;
            ShowNoImage($"Render failed: {ex.Message}");
            SetStatus($"Render failed: {ex.Message}", BpMessageKind.Error);
        }
        finally
        {
            if (round == _round)
            {
                HideBusy();
                ShowPrintState();
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ShowNoImage(string message)
    {
        _rendered = null;
        _renderedType = null;
        _tooLong = false;
        PaperText.Text = "";
        Monochrome.ShowMessage(message);
    }

    private void ShowBusy(string text)
    {
        BusyText.Text = text;
        BusyPanel.Visibility = Visibility.Visible;
    }

    private void HideBusy() => BusyPanel.Visibility = Visibility.Collapsed;

    /// <summary>"192.6 cm of paper", "4.29 m of paper", or "3 pages of 20.0 cm, 60.0 cm of paper".</summary>
    public static string PaperLength(RenderedDocument rendered)
    {
        static double Cm(RenderedPage p) => (p.EffectiveHeightPx > 0 ? p.EffectiveHeightPx : p.HeightPx) / 96.0 * 2.54;
        static string Length(double cm) => cm >= 100
            ? (cm / 100).ToString("0.00", CultureInfo.InvariantCulture) + " m"
            : cm.ToString("0.0", CultureInfo.InvariantCulture) + " cm";

        double total = rendered.Pages.Sum(Cm);
        return rendered.Pages.Count == 1
            ? $"{Length(total)} of paper"
            : $"{rendered.Pages.Count.ToString("#,##0", CultureInfo.InvariantCulture)} pages of {Length(Cm(rendered.Pages[0]))}, {Length(total)} of paper";
    }
}
