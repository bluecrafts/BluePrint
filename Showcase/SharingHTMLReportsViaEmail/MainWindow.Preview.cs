// Retrieve -> render -> preview, after a report or a month is chosen.
//
// Every round opens the report again (a fresh BluePrintDocument: two rounds never
// share rows), reads the month from the database with the FromDate and ToDate
// arguments, and renders it once for the email. The preview control is given the
// same document and draws its pages itself, so its own toolbar - print, export to
// PDF or Excel - works on the month shown.
//
// Choosing quickly cancels the round before; a round that finishes late never
// replaces a newer one.

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BluePrint.Render;
using BluePrint.Render.Models;
using BluePrint.UI.Controls;
using Microsoft.Data.Sqlite;
using SharingHtmlReports.Reports;

namespace SharingHtmlReports;

/// <summary>A report read for a month and rendered: what the email is made from.</summary>
public sealed record ShownReport(ReportEntry Report, MonthEntry Month, BluePrintDocument Document,
                                 RenderedDocument Rendered, int Rows)
{
    public string Title => $"{Report.Name} - {Month.Name}";
}

public partial class MainWindow
{
    // Arrow keys through a list: wait for the choice to settle before reading.
    private readonly DispatcherTimer _retrieveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? _roundCts;
    private int _round;

    /// <summary>The report and month on screen, or null.</summary>
    public ShownReport? Shown { get; private set; }

    /// <summary>The round in progress (for the tests: await it instead of polling).</summary>
    public Task CurrentRound { get; private set; } = Task.CompletedTask;

    private void InitializePreview()
    {
        _retrieveTimer.Tick += (_, _) =>
        {
            _retrieveTimer.Stop();
            CurrentRound = RunRoundAsync();
        };
        ShowPreviewMessage("Select a report and a month.", BpMessageKind.Information);
    }

    private void ScheduleRetrieve()
    {
        _retrieveTimer.Stop();
        _retrieveTimer.Start();
    }

    /// <summary>Reads the selected report and month now.</summary>
    public Task RetrieveNow()
    {
        _retrieveTimer.Stop();
        return CurrentRound = RunRoundAsync();
    }

    private void Retrieve_Executed(object sender, ExecutedRoutedEventArgs e) => RetrieveNow();

    // Asked while InitializeComponent is still building the window: the lists are not there yet.
    private void CanRetrieve_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = IsInitialized && SelectedReport is { CanRetrieve: true } && SelectedMonth is not null && DatabaseExists
                       && RetrieveProgress.Visibility != Visibility.Visible;

    private async Task RunRoundAsync()
    {
        _roundCts?.Cancel();
        var cts = _roundCts = new CancellationTokenSource();
        int round = ++_round;
        var ct = cts.Token;

        var report = SelectedReport;
        var month = SelectedMonth;
        if (report is null || month is null)
        {
            // While the months are still being read there is nothing to choose yet.
            if (_months.Count > 0 || report is null) ShowPreviewMessage("Select a report and a month.", BpMessageKind.Information);
            return;
        }
        if (!report.CanRetrieve)
        {
            ClearShown();
            ShowPreviewMessage(report.Problem!, BpMessageKind.Warning);
            return;
        }

        StatusReportText.Text = $"{report.Name} - {month.Name}";
        SetStatus($"Retrieving {month.Name}...");
        RetrieveProgress.Visibility = Visibility.Visible;
        try
        {
            var document = BluePrintDocument.Load(report.Path);
            await using (var connection = new SqliteConnection(MonthCatalog.ConnectionString()))
                await document.RetrieveAsync(connection, month.Arguments, ct: ct);
            int rows = document.Data.RowCount;

            // The pages the email is made from. Off the UI thread: a long month takes a moment.
            var rendered = await Task.Run(() => document.Render(ct: ct), ct)
                           ?? throw new InvalidOperationException("The report produced no pages.");

            // A newer round started while this one was reading: its result is the one to show.
            if (round != _round) return;

            Shown = new ShownReport(report, month, document, rendered, rows);
            HidePreviewMessage();
            Preview.ReportDocument = document;
            SetStatus(rows == 0
                ? $"No orders in {month.Name} for this report."
                : $"Retrieved {rows.ToString("#,##0", CultureInfo.InvariantCulture)} rows for {month.Name}: " +
                  $"{rendered.Pages.Count} page(s).");
            ApplySubjectTemplate();   // the subject follows the report and the month
            await RebuildEmailNow();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A newer round took over.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (round != _round) return;
            // The report of the previous choice must not stay on screen as if it were this one.
            ClearShown();
            ShowPreviewMessage($"Retrieve failed: {ex.Message}", BpMessageKind.Error);
            SetStatus($"Retrieve failed: {ex.Message}", BpMessageKind.Error);
        }
        finally
        {
            if (round == _round) RetrieveProgress.Visibility = Visibility.Hidden;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ClearShown()
    {
        Shown = null;
        Preview.ReportDocument = null;
        ClearEmail();
    }

    private void ShowPreviewMessage(string text, BpMessageKind kind)
    {
        PreviewMessageText.Text = text;
        PreviewMessageIcon.Kind = kind;
        PreviewMessagePanel.Visibility = Visibility.Visible;
    }

    private void HidePreviewMessage() => PreviewMessagePanel.Visibility = Visibility.Collapsed;
}
