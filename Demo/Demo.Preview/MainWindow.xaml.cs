// The preview control in a WPF window.
//
//   dotnet run --project Demo.Preview
//
// Setting Report renders the definition and shows the first page; setting
// DataProvider gives it something to render. Everything else on screen -
// paging, zoom, print - is the control's own.
//
// The three buttons fill the same report three different ways. What they show
// is that the control does not care which: it takes rows, not a connection.
// The three are spelled out in InvoiceQuery.cs.

using System.Threading.Tasks;
using System.Windows;
using BluePrint.Core.Models;
using BluePrint.Render;
using BluePrint.Render.Data;
using Demo.Shared;

namespace Demo.Preview;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Open on a filled report rather than an empty frame. Pressing a button
        // repeats this with a different way of getting the rows.
        Loaded += async (_, _) => await ShowAsync(RetrieveWay.Retrieve);
    }

    private async void Retrieve_Click(object sender, RoutedEventArgs e)
        => await ShowAsync(RetrieveWay.Retrieve);

    private async void SetSql_Click(object sender, RoutedEventArgs e)
        => await ShowAsync(RetrieveWay.SetSqlThenRetrieve);

    private async void SetDataTable_Click(object sender, RoutedEventArgs e)
        => await ShowAsync(RetrieveWay.SetDataTableThenRetrieve);

    private async Task ShowAsync(RetrieveWay way)
    {
        // A fresh document per press. SetSqlThenRetrieve changes the query the
        // definition carries, so reusing one instance would let that leak into
        // whatever was pressed next.
        var report = BluePrintDocument.Load(InvoiceQuery.ReportPath);

        Status.Text = $"{way}...";
        SetButtonsEnabled(false);
        try
        {
            var rows = await InvoiceQuery.LoadAsync(way, report);

            // CachedDataProvider holds rows the host has already retrieved, so
            // paging and zoom never go back to the database. It carries the
            // retrieval argument values along with them, which is how the
            // heading of this report prints @OrderId.
            var data = new CachedDataProvider(rows);
            data.SetRetrievalArgumentValues(
                RetrievalArgumentScope.Of(report.Definition), InvoiceQuery.ArgumentValues);

            Preview.DataProvider = data;
            Preview.Report = report.Definition;

            Status.Text = $"{way} - {rows.Rows.Count} rows - {InvoiceQuery.Describe(way)}";
        }
        catch (Exception ex)
        {
            Status.Text = $"{way} failed - {ex.Message}";
            MessageBox.Show(this, ex.Message, "Retrieve failed",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        RetrieveButton.IsEnabled = enabled;
        SetSqlButton.IsEnabled = enabled;
        SetDataTableButton.IsEnabled = enabled;
    }
}
