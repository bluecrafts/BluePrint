// The report designer embedded in a host application.
//
//   dotnet run --project Demo.Designer
//
// LoadReport opens a definition on the design surface; Save writes back
// whatever the user edited.
//
// This opens nw-05-invoice.bpt, the same report the other two samples render,
// and retrieves order 11077 straight away, so the bands, the expressions and the
// query behind it can be looked at from the inside with real rows. Load opens
// another report - nw-06-products-by-category.bpt is copied next to the
// executable for that - without retrieving; press Retrieve for its rows. Refresh
// (or F5) renders the preview again after a change to the design. Editing needs
// no database: the data source page shows the query as text.
//
// Retrieving rows is the one thing a host has to wire up itself, because the
// designer never opens a connection. Press Retrieve: the designer asks for the report's arguments, such
// as @OrderId, then hands the host an empty BluePrintDocument for the report
// being designed - the same object Demo.Render and Demo.Preview fill - and the
// host puts the rows in.

using System.IO;
using System.Windows;
using BluePrint.Render.WPF.Dialogs;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Demo.Designer;

public partial class MainWindow : Window
{
    private static readonly string ConnectionString =
        $"Data Source={Path.Combine(AppContext.BaseDirectory, "northwind.db")}";

    public MainWindow()
    {
        InitializeComponent();

        Designer.LoadReport(Path.Combine(AppContext.BaseDirectory, "nw-05-invoice.bpt"));

        // the values the report's query needs (@OrderId) - the dialog every BluePrint
        // host uses; return null to cancel. The session key keeps the values typed
        // for each report apart.
        Designer.RetrievalValuePromptFactory = arguments =>
            RetrievalValueDialog.Prompt(arguments, this, sessionKey: Designer.CurrentFilePath);

        // the rows: the report runs its own query - main query, every lookup and
        // the argument values - on our connection. true = the rows are in;
        // false or an exception keeps the rows of the previous retrieve on screen
        Designer.RetrieveReportFactory = async (report, args) =>
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await report.RetrieveAsync(connection, args);
            return true;
        };

        // The first report retrieves by itself, with the order Demo.Render prints.
        // Passing the values skips the dialog.
        Loaded += (_, _) => Designer.Retrieve(new Dictionary<string, object?> { ["OrderId"] = 11077L });
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Report",
            Filter = "BluePrint report (*.bpt)|*.bpt",
            InitialDirectory = Path.GetDirectoryName(Designer.CurrentFilePath) ?? AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            Designer.LoadReport(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open {Path.GetFileName(dialog.FileName)}.\n\n{ex.Message}", "Load Report",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // No retrieve here: the preview shows the new report's layout, without
        // rows, until Retrieve is pressed.
        Designer.RefreshPreview(resetDocument: true);
    }

    private void Retrieve_Click(object sender, RoutedEventArgs e) => Designer.Retrieve();

    // Renders the preview again from the design as it is now, with the rows
    // already retrieved - the same as F5.
    private void Refresh_Click(object sender, RoutedEventArgs e) => Designer.RefreshPreview(resetDocument: true);
}
