// The report designer embedded in a host application.
//
//   dotnet run --project Demo.Designer
//
// Setting Report opens the definition on the design surface. Read it back from
// the same property to save whatever the user edited.
//
// This opens nw-05-invoice.bpt, the same report the other two samples render,
// so the bands, the expressions and the query behind it can be looked at from
// the inside. Editing needs no database: the data source page shows the query
// as text.
//
// Retrieving rows is the one thing a host has to wire up itself, because the
// designer never opens a connection. Press Retrieve (or F5 after a first
// retrieve to render again): the designer asks for @OrderId, then hands the
// host an empty BluePrintDocument for the report being designed - the same
// object Demo.Render and Demo.Preview fill - and the host puts the rows in.

using System.IO;
using System.Windows;
using BluePrint.Render;
using BluePrint.Render.WPF.Dialogs;
using Microsoft.Data.Sqlite;

namespace Demo.Designer;

public partial class MainWindow : Window
{
    private static readonly string ConnectionString =
        $"Data Source={Path.Combine(AppContext.BaseDirectory, "northwind.db")}";

    public MainWindow()
    {
        InitializeComponent();

        var path = Path.Combine(AppContext.BaseDirectory, "nw-05-invoice.bpt");
        Designer.Report = BluePrintDocument.Load(path).Definition;

        // the values the report's query needs (@OrderId) - the dialog every BluePrint
        // host uses; return null to cancel
        Designer.RetrievalValuePromptFactory = arguments =>
            RetrievalValueDialog.Prompt(arguments, this, sessionKey: path);

        // the rows: the report runs its own query - main query, every lookup and
        // the argument values - on our connection. true = the rows are in;
        // false or an exception keeps the rows of the previous retrieve on screen
        Designer.RetrieveReportFactory = async (report, args) =>
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await report.RetrieveAsync(connection, args);
            return true;
        };
    }

    private void Retrieve_Click(object sender, RoutedEventArgs e) => Designer.Retrieve();
}
