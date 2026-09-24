// The three ways a host can put rows into a BluePrint report, shared by
// Demo.Render and Demo.Preview.
//
// Demo.Render links this file, and so does Demo.Preview, the same way the two
// barcode samples share BarcodeSamples.cs.
//
// BluePrint renders; it does not connect. Whichever way is used below, the host
// is the one that opens the connection and hands the rows over - and the host
// is also the one that references a driver. These samples use the SQLite copy
// of Northwind that ships with the repository, so nothing has to be installed
// or started: the database is a file.
//
//   Retrieve                   The query inside the .bpt, run as it is written.
//                              It contains {bp:...} calls, which are what let
//                              one report run on all six supported databases,
//                              and ReportQueryService renders them for whatever
//                              the connection points at. Nothing here knows or
//                              cares that the answer happens to be SQLite.
//
//   SetSqlThenRetrieve         The same retrieval, but with the query replaced
//                              at run time by SQLite-specific SQL. This is the
//                              shape to use when the host builds the statement
//                              itself and still wants BluePrint to run it.
//
//   SetDataTableThenRetrieve   No BluePrint data layer at all: the host queries
//                              with its own driver and hands over a DataTable.
//                              This is the shape for a host that already has
//                              its own data access, or whose rows never came
//                              from a database in the first place.
//
// All three end in the same place - a DataTable the report reads by column
// name - so they can be compared side by side on one report.

using System.Data;
using System.IO;
using BluePrint.Core.Models;
using BluePrint.DataSources;
using BluePrint.Render;
using Microsoft.Data.Sqlite;

namespace Demo.Shared;

/// <summary>How the rows reach the report. The three are interchangeable.</summary>
public enum RetrieveWay
{
    Retrieve,
    SetSqlThenRetrieve,
    SetDataTableThenRetrieve,
}

/// <summary>One invoice, read from the Northwind database that ships with the repository.</summary>
public static class InvoiceQuery
{
    /// <summary>
    /// The order the samples print. nw-05-invoice.bpt takes the order number as a
    /// retrieval argument, so this is the one value that decides what comes out.
    /// </summary>
    public const long OrderId = 11077;

    /// <summary>Name of the argument the report declares, without the leading @.</summary>
    public const string OrderIdArgument = "OrderId";

    /// <summary>
    /// The query in nw-05-invoice.bpt written out for SQLite. Only three parts of
    /// it differ from what the .bpt holds, and each is a {bp:...} call there:
    ///
    ///     {bp:DateText(o.OrderDate)}                  DATE(o.OrderDate)
    ///     {bp:Concat(e.LastName, ', ', e.FirstName)}  (e.LastName || ', ' || e.FirstName)
    ///     {bp:Name(Order Details)}                    "Order Details"
    ///
    /// The select list is deliberately identical, column for column. SetSql does
    /// not rebuild the report's column definitions - keeping the binding is the
    /// caller's job - so a replacement query has to return the same columns in
    /// the same order.
    /// </summary>
    public const string SqliteSql = """
        SELECT o.OrderID, DATE(o.OrderDate) AS OrderDate, DATE(o.ShippedDate) AS ShippedDate,
               c.CompanyName AS CustomerName, c.Address, c.City, c.PostalCode, c.Country,
               (e.LastName || ', ' || e.FirstName) AS Salesperson,
               sh.CompanyName AS ShipperName, CAST(o.Freight AS REAL) AS Freight,
               p.ProductName, CAST(od.UnitPrice AS REAL) AS UnitPrice,
               od.Quantity, od.Discount,
               od.UnitPrice * od.Quantity * (1 - od.Discount) AS ExtendedPrice
        FROM Orders o
        JOIN Customers c ON c.CustomerID = o.CustomerID
        JOIN Employees e ON e.EmployeeID = o.EmployeeID
        JOIN Shippers sh ON sh.ShipperID = o.ShipVia
        JOIN "Order Details" od ON od.OrderID = o.OrderID
        JOIN Products p ON p.ProductID = od.ProductID
        WHERE o.OrderID = @OrderId
        ORDER BY p.ProductName
        """;

    /// <summary>The report definition, copied next to the executable by the .csproj.</summary>
    public static string ReportPath => Path.Combine(AppContext.BaseDirectory, "nw-05-invoice.bpt");

    /// <summary>The database file, copied next to the executable by the .csproj.</summary>
    public static string DatabasePath => Path.Combine(AppContext.BaseDirectory, "northwind.db");

    /// <summary>
    /// The value every way retrieves with. The report prints it too - the heading
    /// reads @OrderId - so it is handed to the report as well as to the query.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, object?> ArgumentValues =
        new Dictionary<string, object?> { [OrderIdArgument] = OrderId };

    /// <summary>One line saying what a way did, for a status bar or the console.</summary>
    public static string Describe(RetrieveWay way) => way switch
    {
        RetrieveWay.Retrieve => "the query in the .bpt, {bp:...} calls and all",
        RetrieveWay.SetSqlThenRetrieve => "SQLite SQL set on the report, then retrieved",
        RetrieveWay.SetDataTableThenRetrieve => "queried with Microsoft.Data.Sqlite, handed over as a DataTable",
        _ => way.ToString(),
    };

    /// <summary>
    /// Retrieves the rows one of the three ways. The report is passed in because
    /// two of them read or change what it carries: the query, and the arguments
    /// that query declares.
    /// </summary>
    public static Task<DataTable> LoadAsync(RetrieveWay way, BluePrintDocument report,
                                            CancellationToken ct = default) => way switch
    {
        RetrieveWay.Retrieve                 => RetrieveAsync(report, ct),
        RetrieveWay.SetSqlThenRetrieve       => SetSqlThenRetrieveAsync(report, ct),
        RetrieveWay.SetDataTableThenRetrieve => Task.FromResult(QueryWithTheDriverDirectly()),
        _ => throw new ArgumentOutOfRangeException(nameof(way)),
    };

    // -- 1. the query the report already carries -------------------------------

    private static Task<DataTable> RetrieveAsync(BluePrintDocument report, CancellationToken ct)
    {
        RegisterDriver();
        return new ReportQueryService()
            .ExecuteAsync(Profile, report.Definition.DataSource!, ArgumentValues, ct);
    }

    // -- 2. a query of the host's own, run by BluePrint ------------------------

    private static Task<DataTable> SetSqlThenRetrieveAsync(BluePrintDocument report, CancellationToken ct)
    {
        RegisterDriver();

        // The arguments go in with the SQL: they become the report's whole
        // argument list, and every @name in the new statement has to be in it.
        report.SetSql(SqliteSql, new RetrievalArgument(OrderIdArgument, RetrievalArgumentType.Number));

        return new ReportQueryService()
            .ExecuteAsync(Profile, report.Definition.DataSource!, ArgumentValues, ct);
    }

    // -- 3. the host's own data access, BluePrint not involved -----------------

    private static DataTable QueryWithTheDriverDirectly()
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = SqliteSql;
        command.Parameters.AddWithValue("@" + OrderIdArgument, OrderId);

        using var reader = command.ExecuteReader();

        // Columns are declared as object rather than left to the reader to infer:
        // ShippedDate is null on an order that has not shipped, and a column whose
        // first row is null has no type to infer from.
        var table = new DataTable();
        for (int i = 0; i < reader.FieldCount; i++)
            table.Columns.Add(reader.GetName(i), typeof(object));

        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            table.Rows.Add(values);
        }

        return table;
    }

    // -- what the two BluePrint-run ways need ----------------------------------

    /// <summary>
    /// Where to connect. For SQLite that is the path to the file; every other
    /// database fills in Server, Database and credentials instead.
    /// </summary>
    private static ConnectionProfile Profile => new()
    {
        Name         = "Northwind",
        ProviderType = DatabaseProviderType.SQLite,
        DataSource   = DatabasePath,
    };

    /// <summary>
    /// BluePrint ships no ADO.NET driver: the host picks one, references it, and
    /// registers it. Registering the same factory again is a no-op, so this can
    /// sit on the path that needs it instead of in start-up code far away.
    /// </summary>
    private static void RegisterDriver() =>
        BpDataProviders.Register(DatabaseProviderType.SQLite, SqliteFactory.Instance);
}
