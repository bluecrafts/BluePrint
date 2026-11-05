// Retrieve -> preview: what happens after a form or a document is chosen, and
// after a language is chosen.
//
//   - A form or a document: a new round opens the form with its translation
//     layers (a fresh BluePrintDocument - two rounds never share the rows of one
//     report), reads the document in the background and hands the result to the
//     preview. Choosing quickly cancels the round before, and a round that
//     finishes late never replaces a newer one.
//   - A language: the document on screen is rendered again in it. Nothing is
//     read again: the rows are already in the document, and the language only
//     changes the texts, the culture and the fonts. The page shown stays.

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BluePrint.Core.Models;
using BluePrint.Render.WPF.Controls;
using BluePrint.UI.Controls;
using LocalizationShowcase.Documents;
using LocalizationShowcase.Forms;
using Microsoft.Data.Sqlite;

namespace LocalizationShowcase;

public partial class MainWindow
{
    // Arrow keys through the Documents list, or a language button pressed a few times
    // in a row: wait for the choice to settle before doing the work.
    private readonly DispatcherTimer _retrieveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _languageTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private CancellationTokenSource? _roundCts;
    private int _round;

    // The form chosen in the list, opened: its languages and cultures for the Language list.
    private OpenedForm? _openForm;

    // The document on screen, with its rows. A new language renders it again.
    private OpenedForm? _shown;

    // The page to come back to after the preview renders the document again in another language.
    private int _restorePage;

    /// <summary>
    /// Called after a document has been read and before it is shown. The application does not use it;
    /// the tests do, to make one read finish after a newer one - with the token (the old read is
    /// cancelled) or without it (a data source that does not stop when asked).
    /// </summary>
    public Func<OrderDocument, CancellationToken, Task>? BeforeShow { get; set; }

    private void InitializePreview()
    {
        _retrieveTimer.Tick += (_, _) =>
        {
            _retrieveTimer.Stop();
            _ = RunRoundAsync();
        };
        _languageTimer.Tick += (_, _) =>
        {
            _languageTimer.Stop();
            RenderInLanguage();
        };

        // The control goes back to page 1 when it gets a new picture; after a change of
        // language, go back to the page that was shown (when the document still has it).
        DependencyPropertyDescriptor.FromProperty(BluePrintPreviewControl.DocumentProperty, typeof(BluePrintPreviewControl))
            .AddValueChanged(Preview, (_, _) => RestorePage());

    }

    private void ScheduleRetrieve()
    {
        _retrieveTimer.Stop();
        _retrieveTimer.Start();
    }

    private void RetrieveNow()
    {
        _retrieveTimer.Stop();
        _ = RunRoundAsync();
    }

    private void Retrieve_Executed(object sender, ExecutedRoutedEventArgs e) => RetrieveNow();

    private void CanRetrieve_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = SelectedForm is not null && SelectedDocument is not null && DatabaseExists
                       && RetrieveProgress?.Visibility != Visibility.Visible;

    private async Task RunRoundAsync()
    {
        _roundCts?.Cancel();
        var cts = _roundCts = new CancellationTokenSource();
        int round = ++_round;
        var ct = cts.Token;

        var form = SelectedForm;
        var document = SelectedDocument;
        if (form is null || _openForm is null) return;
        if (document is null || !DatabaseExists)
        {
            // While the orders are still being read there is nothing to choose yet: say nothing.
            if (_documents.Count == 0 && DatabaseExists) return;
            ShowPreviewMessage("Select an order to see the invoice.", BpMessageKind.Information);
            return;
        }

        RememberChoice(form, document);
        RetrieveProgress.Visibility = Visibility.Visible;
        try
        {
            // A fresh copy for every round, with its translation layers. Problems with the
            // layers were reported when the form was chosen; here they are only left out.
            var opened = FormLoader.Open(form);
            var definition = opened.Document.Definition;
            bool reads = definition.DataSource?.DataSourceType == DataSourceType.DBSQL
                         && !string.IsNullOrWhiteSpace(definition.DataSource.Sql);

            if (reads)
            {
                await using var conn = new SqliteConnection(OrderCatalog.ConnectionString());
                await opened.Document.RetrieveAsync(conn, OrderCatalog.ArgumentsFor(document.Document, definition), ct: ct);
                if (BeforeShow is { } hook) await hook(document.Document, ct);
            }
            // A newer round started while this one was reading: its result is the one to show.
            if (round != _round) return;

            _shown = opened;
            _restorePage = 0;
            opened.Document.Language = CurrentLanguage.Tag;
            HidePreviewMessage();
            Preview.ReportDocument = opened.Document;   // the control renders it, in the language of the document

            SetStatus(reads
                ? $"Retrieved {opened.Document.Data.RowCount.ToString("#,##0", CultureInfo.InvariantCulture)} lines for {document.TitleText}."
                : "This form does not read from a database.");
            ShowLanguageStatus();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A newer round took over.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (round != _round) return;
            // The invoice of the previous choice must not stay on screen as if it were this one.
            _shown = null;
            Preview.ReportDocument = null;
            ShowPreviewMessage($"Retrieve failed: {ex.Message}", BpMessageKind.Error);
            SetStatus($"Retrieve failed: {ex.Message}", BpMessageKind.Error);
        }
        finally
        {
            if (round == _round) RetrieveProgress.Visibility = Visibility.Hidden;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ScheduleLanguageRender()
    {
        _languageTimer.Stop();
        _languageTimer.Start();
    }

    // The same document, the same rows, another language: no retrieve.
    private void RenderInLanguage()
    {
        if (_shown is null) return;
        var tag = CurrentLanguage.Tag;
        if (string.Equals(_shown.Document.Language, tag, StringComparison.OrdinalIgnoreCase)) return;

        _restorePage = Preview.CurrentPage;
        _shown.Document.Language = tag;
        Preview.Refresh();
        SetStatus($"Showing {_shown.Entry.Name} in {CurrentLanguage.Name}.");
    }

    private void RestorePage()
    {
        if (_restorePage <= 1 || Preview.Document is null) return;
        int page = Math.Min(_restorePage, Preview.TotalPages);
        _restorePage = 0;
        // After the control has set its own page 1 for the new picture.
        Dispatcher.BeginInvoke(() => Preview.CurrentPage = page, DispatcherPriority.Background);
    }

    private void ShowPreviewMessage(string text, BpMessageKind kind)
    {
        PreviewMessageText.Text = text;
        PreviewMessageIcon.Kind = kind;
        PreviewMessagePanel.Visibility = Visibility.Visible;
    }

    private void HidePreviewMessage() => PreviewMessagePanel.Visibility = Visibility.Collapsed;
}
