// The Documents list: the sample documents of SampleDocuments.cs, each one a set
// of retrieval arguments. Choosing one reads it into the selected form. The line
// count and the total come from the database when the application starts.

using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace ReceiptPrinting;

/// <summary>One row of the Documents list.</summary>
public sealed class DocumentEntry(SampleDocument document) : INotifyPropertyChanged
{
    private DocumentSummary? _summary;

    public SampleDocument Document { get; } = document;
    public string Name => Document.Name;
    public string Group => Document.Group;

    // "..." until the database has been read.
    public string LinesText => _summary is { } s ? s.Lines.ToString("#,##0", CultureInfo.InvariantCulture) : "...";
    public string TotalText => _summary is { } s ? s.Total.ToString("#,##0.00", CultureInfo.InvariantCulture) : "...";

    public string ToolTipText
    {
        get
        {
            var d = Document;
            if (d.FromOrder == d.ToOrder)
                return _summary is { } s ? $"Order {d.FromOrder} - {s.Customer} - {s.OrderDay}" : $"Order {d.FromOrder}";
            var text = $"Orders {d.FromOrder} to {d.ToOrder}, {d.Lines.ToString("#,##0", CultureInfo.InvariantCulture)} lines";
            // Northwind has 2,155 lines in all; the forms' SQL goes through them again after that.
            return d.Lines > 2155 ? text + "\nLines repeat after line 2,155 (the whole database)." : text;
        }
    }

    public DocumentSummary? Summary
    {
        get => _summary;
        set
        {
            _summary = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));   // every column
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // The name screen readers and UI Automation give the row.
    public override string ToString() => Name;
}

public partial class MainWindow
{
    private const string DefaultDocument = "Order 10248";

    private readonly List<DocumentEntry> _documents = [.. SampleDocuments.All.Select(d => new DocumentEntry(d))];
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private DocumentEntry? SelectedDocument => DocumentsGrid.SelectedItem as DocumentEntry;

    private static bool DatabaseExists => File.Exists(SampleDocuments.DatabasePath);

    private void InitializeDocuments()
    {
        var view = CollectionViewSource.GetDefaultView(_documents);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DocumentEntry.Group)));
        view.Filter = o => o is DocumentEntry d && d.Name.Contains(DocumentSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
        DocumentsGrid.ItemsSource = view;
        ShowDocumentCount();

        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            view.Refresh();
            ShowDocumentCount();
        };

        if (!DatabaseExists)
        {
            ShowNoDatabase("northwind.db was not found next to the application. Build the project again to copy it.");
            return;
        }
        _ = LoadSummariesAsync();
    }

    // The numbers of the list, read once in the background; the list is usable meanwhile.
    private async Task LoadSummariesAsync()
    {
        try
        {
            var summaries = await Task.Run(() => SampleDocuments.SummarizeAsync());
            foreach (var entry in _documents)
                if (summaries.TryGetValue(entry.Name, out var summary)) entry.Summary = summary;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowNoDatabase($"Could not read the documents from northwind.db: {ex.Message}");
        }
    }

    private void ShowNoDatabase(string message)
    {
        NoDatabaseText.Text = message;
        NoDatabasePanel.Visibility = Visibility.Visible;
        DocumentsGrid.Visibility = Visibility.Collapsed;
    }

    private void ShowDocumentCount()
    {
        int shown = CollectionViewSource.GetDefaultView(_documents).Cast<object>().Count();
        DocumentCountText.Text = shown == _documents.Count
            ? $"{_documents.Count} documents"
            : $"{shown} of {_documents.Count} documents";
    }

    private void SelectDocument(string name)
    {
        var entry = _documents.FirstOrDefault(d => d.Name == name);
        if (entry is null) return;
        DocumentsGrid.SelectedItem = entry;
        DocumentsGrid.ScrollIntoView(entry);
    }

    private void DocumentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        DocumentSearchBox.Focus();
        DocumentSearchBox.SelectAll();
    }

    private void DocumentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowPrintState();
        if (SelectedDocument is null) return;
        ScheduleRetrieve();
    }

    // Enter and a double click read the document again - the same as Retrieve (Ctrl+R).
    private void DocumentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedDocument is not null) RetrieveNow();
    }

    private void DocumentsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || SelectedDocument is null) return;
        RetrieveNow();
        e.Handled = true;   // Enter would otherwise move to the next row
    }
}
