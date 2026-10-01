// Smallest thing that renders a BluePrint report with real data.
//
//   dotnet run --project Demo.Render
//
// BluePrint is BluePrintDocument-centric: one BluePrintDocument is the centre of
// everything. Each block below follows the same three steps on it, and writes a
// PDF next to the executable:
//
//   1. create the report and load it   2. put the rows in it   3. export it
//
// Only step 2 differs from block to block - it is where the database comes in.
// Demo.Preview shows the same four ways as four buttons. Here, with no UI to
// click, all four run one after another - which is also the point: the four
// are interchangeable, and the four files coming out alike is what says so.
//
// The report is nw-05-invoice.bpt, one invoice chosen by the order number it
// declares as a retrieval argument. The database is the SQLite copy of
// Northwind that ships with the repository, so nothing has to be installed.

using BluePrint.Core.Models;
using BluePrint.DataSources;
using BluePrint.Render;
using Microsoft.Data.Sqlite;

var reportPath       = Path.Combine(AppContext.BaseDirectory, "nw-05-invoice.bpt");
var databasePath     = Path.Combine(AppContext.BaseDirectory, "northwind.db");
var connectionString = $"Data Source={databasePath}";

// nw-05-invoice.bpt asks for one retrieval argument: the order to print.
var arguments = new Dictionary<string, object?> { ["OrderId"] = 11077L };

// ----------------------------------------------------------------------------
// Way 1: Own DbConnection - the report retrieves on a connection we give it.
//
// The report runs the query it carries - the main query and every lookup query,
// {bp:...} calls and @OrderId included. A closed connection is opened for the
// retrieve and closed again; an open one (perhaps with a transaction: pass
// new DbQueryOptions { Transaction = tx }) is used and left open. No driver to
// register: BluePrint works out the database from the connection.
// ----------------------------------------------------------------------------
{
    // 1. create the report, then load the .bpt into it
    var report = new BluePrintDocument();
    report.LoadBpt(File.ReadAllText(reportPath));

    // 2. retrieve - connect, run the query, disconnect
    await using var connection = new SqliteConnection(connectionString);
    await report.RetrieveAsync(connection, arguments);

    // 3. export
    Export(report, "OwnConnection", "the query in the .bpt, on a connection we gave the report");
}

// ----------------------------------------------------------------------------
// Way 2: Connection Profile - the report connects by itself.
//
// Describe the database once; the report opens a connection, runs the query it
// carries, and closes it again. BluePrint ships no driver, so the application
// registers the one it references.
// ----------------------------------------------------------------------------
{
    // 1. create the report, then load the .bpt into it
    var report = new BluePrintDocument();
    report.LoadBpt(File.ReadAllText(reportPath));

    // 2. retrieve - describe the database and register its driver; the report connects and disconnects
    BpDataProviders.Register(DatabaseProviderType.SQLite, SqliteFactory.Instance);
    var profile = new ConnectionProfile
    {
        Name         = "Northwind",
        ProviderType = DatabaseProviderType.SQLite,
        DataSource   = databasePath,
    };
    await report.RetrieveAsync(profile, arguments);

    // 3. export
    Export(report, "Profile", "the query in the .bpt, the report connecting by itself");
}

// ----------------------------------------------------------------------------
// Way 3: Own DbConnection + SetSql - our SQL, retrieved by the report.
//
// SetSql replaces the query in the .bpt for this run. It does not rebuild the
// report's columns, so the new query must return the same columns in the same
// order. This SQL is written for SQLite; the one in the .bpt uses {bp:...}
// calls so that it runs on every supported database.
// ----------------------------------------------------------------------------
{
    // 1. create the report, load the .bpt into it, and give it our SQL
    var report = new BluePrintDocument();
    report.LoadBpt(File.ReadAllText(reportPath));
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

    // 2. retrieve - connect, run the query, disconnect
    await using var connection = new SqliteConnection(connectionString);
    await report.RetrieveAsync(connection, arguments);

    // 3. export
    Export(report, "OwnConnectionSetSql", "our own SQLite SQL, set on the report, then retrieved");
}

// ----------------------------------------------------------------------------
// Way 4: Own Query + List - we do all the database work, the report gets objects.
//
// BluePrint's data layer is not involved: we query with the driver (or an ORM
// such as Entity Framework or Dapper) and hand over a List<T>. Property names
// are matched to the report's column names, ignoring case - see InvoiceLine at
// the bottom of this file. Do not run the SQL from the .bpt this way: its
// {bp:...} calls are only translated when the report retrieves.
// ----------------------------------------------------------------------------
{
    // 1. create the report, then load the .bpt into it
    var report = new BluePrintDocument();
    report.LoadBpt(File.ReadAllText(reportPath));

    // 2. our own query - connect, read into objects, disconnect
    var lines = new List<InvoiceLine>();
    await using (var connection = new SqliteConnection(connectionString))
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
        command.Parameters.AddWithValue("@OrderId", arguments["OrderId"]);

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
    report.SetArgumentValues(arguments);

    // 3. export
    Export(report, "OwnQueryList", "queried with Microsoft.Data.Sqlite, handed over as a List<InvoiceLine>");
}

Console.WriteLine();
Console.WriteLine($"Written to {AppContext.BaseDirectory}");

// ----------------------------------------------------------------------------
// 3. export - the report uses the rows it holds, so there is nothing to pass.
//
// ExportToExcel, ExportToHtml, ExportToCsv and ExportToImage work the same way.
// To write into a stream instead of a file, for example from a web service:
//
//     report.ExportToPdf(responseStream, ct: cancellationToken);
//
// PDF options (password, PDF/A-3, page range) go in as
//
//     report.ExportToPdf(path, options: new PdfExportOptions { ... });
// ----------------------------------------------------------------------------
static void Export(BluePrintDocument report, string name, string description)
{
    var outputPath = Path.Combine(AppContext.BaseDirectory, $"Invoice-{name}.pdf");
    report.ExportToPdf(outputPath);

    var rows = report.DataProvider?.GetRows("")?.Count ?? 0;
    Console.WriteLine($"{name,-22} {rows,3} rows  ->  {Path.GetFileName(outputPath)}");
    Console.WriteLine($"{"",-22}     {description}");
}

// One invoice line, as way 4 builds it.
//
// The property names are the column names the report declares - not the names
// in the database. nw-05-invoice.bpt was built from a query over several tables,
// so a column taken straight from a table carries the table's name
// (Products_ProductName), and a computed one carries its alias (ExtendedPrice).
// Studio's Column List shows a report's column names.
sealed class InvoiceLine
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
