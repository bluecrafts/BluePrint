// The Documents list: two batches, then every order of the Northwind database,
// read in the background when the application starts. Choosing one reads it into
// the selected form.

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LocalizationShowcase.Documents;

namespace LocalizationShowcase;

/// <summary>One row of the Documents list.</summary>
public sealed class DocumentEntry(OrderDocument document, OrderSummary? summary, IReadOnlyList<OrderSummary>? batchOrders = null)
{
    public OrderDocument Document { get; } = document;
    public OrderSummary? Summary { get; } = summary;
    public string Name => Document.Name;
    public string Group => Document.Group;

    public bool IsBatch => Document.Group == OrderCatalog.Batches;

    // A batch: "10248-10257" in the Order column, "10 invoices" in the Customer column.
    public string OrderText => IsBatch
        ? $"{Document.FromOrder}-{Document.ToOrder}"
        : Document.FromOrder.ToString(CultureInfo.InvariantCulture);
    public string Customer => IsBatch ? $"{Document.ToOrder - Document.FromOrder + 1} invoices" : Summary?.Customer ?? "";
    public string Country => Summary?.Country ?? "";

    /// <summary>What the title bar calls it: "Order 10248" or the batch.</summary>
    public string TitleText => IsBatch ? Document.Name : $"Order {Document.FromOrder}";

    public string ToolTipText
    {
        get
        {
            if (Summary is { } s)
                return $"Order {s.OrderId} - {s.Customer} - {s.Country} - {s.Lines} lines";
            var d = Document;
            var german = (batchOrders ?? []).Where(o => o.OrderId >= d.FromOrder && o.OrderId <= d.ToOrder
                                                         && o.Country is "Germany" or "Austria" or "Switzerland")
                                            .Select(o => o.OrderId.ToString(CultureInfo.InvariantCulture)).ToList();
            return $"Orders {d.FromOrder} to {d.ToOrder}: {d.ToOrder - d.FromOrder + 1} invoices, one per page group."
                   + (german.Count > 0 ? $" German-speaking customers: {string.Join(", ", german)}." : "");
        }
    }

    /// <summary>The search box looks in the order number, the customer and the country. Batches always show.</summary>
    public bool Matches(string text) =>
        IsBatch || text.Length == 0
        || OrderText.Contains(text, StringComparison.OrdinalIgnoreCase)
        || Customer.Contains(text, StringComparison.OrdinalIgnoreCase)
        || Country.Contains(text, StringComparison.OrdinalIgnoreCase);

    // The name screen readers and UI Automation give the row.
    public override string ToString() => TitleText;
}

public partial class MainWindow
{
    private const string DefaultDocument = "10248";

    private readonly List<DocumentEntry> _documents = [];
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private DocumentEntry? SelectedDocument => DocumentsGrid?.SelectedItem as DocumentEntry;

    private static bool DatabaseExists => File.Exists(OrderCatalog.DatabasePath);

    private void InitializeDocuments()
    {
        var view = CollectionViewSource.GetDefaultView(_documents);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DocumentEntry.Group)));
        view.Filter = o => o is DocumentEntry d && d.Matches(DocumentSearchBox.Text.Trim());
        DocumentsGrid.ItemsSource = view;

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
        DocumentCountText.Text = "Loading orders...";
        _ = LoadOrdersAsync();
    }

    // The orders of the list, read once in the background. The documents of last time
    // are selected when they are there.
    private async Task LoadOrdersAsync()
    {
        try
        {
            var orders = await Task.Run(() => OrderCatalog.ReadOrdersAsync());
            var byId = orders.ToDictionary(o => o.OrderId);
            _documents.Clear();
            foreach (var document in OrderCatalog.Documents(orders))
                _documents.Add(new DocumentEntry(document,
                    document.Group == OrderCatalog.Orders ? byId[document.FromOrder] : null, orders));
            CollectionViewSource.GetDefaultView(_documents).Refresh();
            // after the rows are laid out, not before
            _ = Dispatcher.BeginInvoke(FitDocumentColumns, DispatcherPriority.ContextIdle);
            ShowDocumentCount();
            SelectDocument(_config.LastDocument ?? DefaultDocument);
            if (SelectedDocument is null) SelectDocument(DefaultDocument);
            // The first invoice: no need to wait for the selection to settle, as for arrow keys.
            if (SelectedDocument is not null) RetrieveNow();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowNoDatabase($"Could not read northwind.db: {ex.Message}");
        }
    }

    // The list is virtualized while grouped (832 rows would take seconds to lay out
    // otherwise), and a grouped, virtualized DataGrid sizes its columns once, before any
    // row is there, to the 20 px minimum - and keeps them there. Setting each width
    // again once the rows are in makes it size them with the room it really has.
    // (The same value would not count as a change: go through Auto and back.)
    private void FitDocumentColumns()
    {
        foreach (var column in DocumentsGrid.Columns)
        {
            var width = column.Width;
            column.Width = DataGridLength.Auto;
            column.Width = width;
        }
    }

    private void ShowNoDatabase(string message)
    {
        NoDatabaseText.Text = message;
        NoDatabasePanel.Visibility = Visibility.Visible;
        DocumentsGrid.Visibility = Visibility.Collapsed;
        DocumentCountText.Text = "";
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
        if (SelectedDocument is null) return;
        UpdateTitle();
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
