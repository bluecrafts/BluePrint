// The preview control in a WPF window.
//
//   dotnet run --project Demo.Preview
//
// BluePrint is BluePrintDocument-centric: one BluePrintDocument is the centre of
// everything. Each button follows the same three steps on it, top to bottom:
//
//   1. create the report and load it   2. put the rows in it   3. show it
//
// Only step 2 differs from button to button - it is where the database comes in.
// The code repeats from button to button on purpose: read any one handler and
// you have everything that way needs. The database is the SQLite copy of
// Northwind that ships with the repository, so nothing has to be installed.
//
//   Own DbConnection            we give the report a connection, it retrieves
//   Connection Profile          we describe the database, the report connects
//   Own DbConnection + SetSql   as the first, with our own SQL instead of the .bpt's
//   Own Query + List            we query ourselves and give the report a List<T>

using System.IO;
using System.Windows;
using BluePrint.Core.Models;
using BluePrint.DataSources;
using BluePrint.Render;
using Microsoft.Data.Sqlite;

namespace Demo.Preview;

public partial class MainWindow : Window
{
    // The report and the database, both copied next to the executable by the .csproj.
    private static readonly string ReportPath       = Path.Combine(AppContext.BaseDirectory, "nw-05-invoice.bpt");
    private static readonly string ConnectionString = $"Data Source={Path.Combine(AppContext.BaseDirectory, "northwind.db")}";

    // nw-05-invoice.bpt asks for one retrieval argument: the order to print.
    private static readonly Dictionary<string, object?> Arguments = new() { ["OrderId"] = 11077L };

    public MainWindow()
    {
        InitializeComponent();

        // Open on a filled report rather than an empty frame.
        Loaded += (_, _) => OwnConnection_Click(this, new RoutedEventArgs());
    }

    // ------------------------------------------------------------------------
    // Own DbConnection - the report retrieves on a connection we give it.
    //
    // The report runs the query it carries - the main query and every lookup
    // query, {bp:...} calls and @OrderId included. A closed connection is opened
    // for the retrieve and closed again; an open one (perhaps with a transaction:
    // pass new DbQueryOptions { Transaction = tx }) is used and left open.
    // No driver to register: BluePrint works out the database from the connection.
    // ------------------------------------------------------------------------
    private async void OwnConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. create the report, then load the .bpt into it
            var report = new BluePrintDocument();
            report.LoadBpt(File.ReadAllText(ReportPath));

            // 2. retrieve - connect, run the .bpt's query, disconnect
            await using var connection = new SqliteConnection(ConnectionString);
            await report.RetrieveAsync(connection, Arguments);

            // 3. show - the report keeps the rows it retrieved
            Preview.ReportDocument = report;
            Status.Text = $"Own DbConnection - {RowCount(report)} rows";
        }
        catch (Exception ex)
        {
            ShowError("Own DbConnection", ex);
        }
    }

    // ------------------------------------------------------------------------
    // Connection Profile - the report connects by itself.
    //
    // Describe the database once; the report opens a connection, runs the query
    // it carries, and closes it again. BluePrint ships no driver, so the
    // application registers the one it references.
    // ------------------------------------------------------------------------
    private async void Profile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. create the report, then load the .bpt into it
            var report = new BluePrintDocument();
            report.LoadBpt(File.ReadAllText(ReportPath));

            // 2. retrieve - describe the database and register its driver;
            //    the report connects and disconnects
            BpDataProviders.Register(DatabaseProviderType.SQLite, SqliteFactory.Instance);
            var profile = new ConnectionProfile
            {
                Name         = "Northwind",
                ProviderType = DatabaseProviderType.SQLite,
                DataSource   = Path.Combine(AppContext.BaseDirectory, "northwind.db"),
            };
            await report.RetrieveAsync(profile, Arguments);

            // 3. show
            Preview.ReportDocument = report;
            Status.Text = $"Connection Profile - {RowCount(report)} rows";
        }
        catch (Exception ex)
        {
            ShowError("Connection Profile", ex);
        }
    }

    // ------------------------------------------------------------------------
    // Own DbConnection + SetSql - our SQL, retrieved by the report.
    //
    // SetSql replaces the query in the .bpt for this run. It does not rebuild the
    // report's columns, so the new query must return the same columns in the same
    // order. This SQL is written for SQLite; the one in the .bpt uses {bp:...}
    // calls so that it runs on every supported database.
    // ------------------------------------------------------------------------
    private async void OwnConnectionSetSql_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. create the report, load the .bpt into it, and give it our SQL
            var report = new BluePrintDocument();
            report.LoadBpt(File.ReadAllText(ReportPath));
            report.SetSql("""
                SELECT o.OrderID, DATE(o.OrderDate) AS OrderDate, DATE(o.ShippedDate) AS ShippedDate,
                       c.CompanyName AS CustomerName, c.Address, c.City, c.PostalCode, c.Country,
                       (e.LastName || ', ' || e.FirstName) AS Salesperson,
                       sh.CompanyName AS ShipperName, CAST(o.Freight AS REAL) AS Freight,
                       p.ProductName, CAST(od.UnitPrice AS REAL) AS UnitPrice,
                       od.Quantity AS LineQuantity, od.Discount AS LineDiscount,
                       od.UnitPrice * od.Quantity * (1 - od.Discount) AS ExtendedPrice
                FROM Orders o
                JOIN Customers c ON c.CustomerID = o.CustomerID
                JOIN Employees e ON e.EmployeeID = o.EmployeeID
                JOIN Shippers sh ON sh.ShipperID = o.ShipVia
                JOIN "Order Details" od ON od.OrderID = o.OrderID
                JOIN Products p ON p.ProductID = od.ProductID
                WHERE o.OrderID = @OrderId
                ORDER BY p.ProductName
                """,
                new RetrievalArgument("OrderId", RetrievalArgumentType.Number));

            // 2. retrieve - our SQL this time, on a connection we give it
            await using var connection = new SqliteConnection(ConnectionString);
            await report.RetrieveAsync(connection, Arguments);

            // 3. show
            Preview.ReportDocument = report;
            Status.Text = $"Own DbConnection + SetSql - {RowCount(report)} rows";
        }
        catch (Exception ex)
        {
            ShowError("Own DbConnection + SetSql", ex);
        }
    }

    // ------------------------------------------------------------------------
    // Own Query + List - we do all the database work, the report gets objects.
    //
    // BluePrint's data layer is not involved: we query with the driver (or an
    // ORM such as Entity Framework or Dapper) and hand over a List<T>. Property
    // names are matched to the report's column names, ignoring case - see
    // InvoiceLine at the bottom of this file.
    // Do not run the SQL from the .bpt this way - its {bp:...} calls are only
    // translated when the report retrieves; use one of the buttons above for that.
    // ------------------------------------------------------------------------
    private async void OwnQuery_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. create the report, then load the .bpt into it
            var report = new BluePrintDocument();
            report.LoadBpt(File.ReadAllText(ReportPath));

            // 2. our own query - connect, read into objects, disconnect
            var lines = new List<InvoiceLine>();
            await using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT o.OrderID, DATE(o.OrderDate) AS OrderDate, DATE(o.ShippedDate) AS ShippedDate,
                           c.CompanyName AS CustomerName, c.Address, c.City, c.PostalCode, c.Country,
                           (e.LastName || ', ' || e.FirstName) AS Salesperson,
                           sh.CompanyName AS ShipperName, o.Freight,
                           p.ProductName, od.UnitPrice, od.Quantity, od.Discount
                    FROM Orders o
                    JOIN Customers c ON c.CustomerID = o.CustomerID
                    JOIN Employees e ON e.EmployeeID = o.EmployeeID
                    JOIN Shippers sh ON sh.ShipperID = o.ShipVia
                    JOIN "Order Details" od ON od.OrderID = o.OrderID
                    JOIN Products p ON p.ProductID = od.ProductID
                    WHERE o.OrderID = @OrderId
                    ORDER BY p.ProductName
                    """;
                command.Parameters.AddWithValue("@OrderId", Arguments["OrderId"]);

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var unitPrice = Convert.ToDouble(reader["UnitPrice"]);
                    var quantity  = Convert.ToInt64(reader["Quantity"]);
                    var discount  = Convert.ToDouble(reader["Discount"]);
                    lines.Add(new InvoiceLine
                    {
                        Orders_OrderID       = Convert.ToInt64(reader["OrderID"]),
                        OrderDate            = reader["OrderDate"] as string,
                        ShippedDate          = reader["ShippedDate"] as string,
                        CustomerName         = reader["CustomerName"] as string,
                        Customers_Address    = reader["Address"] as string,
                        Customers_City       = reader["City"] as string,
                        Customers_PostalCode = reader["PostalCode"] as string,
                        Customers_Country    = reader["Country"] as string,
                        Salesperson          = reader["Salesperson"] as string,
                        ShipperName          = reader["ShipperName"] as string,
                        Freight              = Convert.ToDouble(reader["Freight"]),
                        Products_ProductName = reader["ProductName"] as string,
                        UnitPrice            = unitPrice,
                        LineQuantity         = quantity,
                        LineDiscount         = discount,
                        ExtendedPrice        = unitPrice * quantity * (1 - discount),
                    });
                }
            }

            // the rows, and the argument value a retrieve would keep (@OrderId for any expression that prints it)
            report.SetData(lines);
            report.SetArgumentValues(Arguments);

            // 3. show
            Preview.ReportDocument = report;
            Status.Text = $"Own Query + List - {lines.Count} rows";
        }
        catch (Exception ex)
        {
            ShowError("Own Query + List", ex);
        }
    }

    // One invoice line, as the Own Query + List button builds it.
    //
    // The property names are the column names the report declares - not the names
    // in the database. nw-05-invoice.bpt was built from a query over several
    // tables, so a column taken straight from a table carries the table's name
    // (Products_ProductName), and a computed one carries its alias (ExtendedPrice).
    // Studio's Column List shows a report's column names.
    private sealed class InvoiceLine
    {
        public long    Orders_OrderID       { get; init; }
        public string? OrderDate            { get; init; }
        public string? ShippedDate          { get; init; }
        public string? CustomerName         { get; init; }
        public string? Customers_Address    { get; init; }
        public string? Customers_City       { get; init; }
        public string? Customers_PostalCode { get; init; }
        public string? Customers_Country    { get; init; }
        public string? Salesperson          { get; init; }
        public string? ShipperName          { get; init; }
        public double  Freight              { get; init; }
        public string? Products_ProductName { get; init; }
        public double  UnitPrice            { get; init; }
        public long    LineQuantity         { get; init; }
        public double  LineDiscount         { get; init; }
        public double  ExtendedPrice        { get; init; }
    }

    private static int RowCount(BluePrintDocument report) => report.DataProvider?.GetRows("")?.Count ?? 0;

    private void ShowError(string way, Exception ex)
    {
        Status.Text = $"{way} failed - {ex.Message}";
        MessageBox.Show(this, ex.Message, "Retrieve failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
