// The documents of the Documents list: each one is a set of retrieval arguments
// for the forms in Samples, read from the Northwind database of this repository.
//
// Northwind has no order longer than 25 lines and 2,155 lines in all, so a long
// document is a range of orders one after another, and the 5,000-line one goes
// through the whole database three times (the forms' SQL joins Shippers, which
// has three rows on every database, and orders by that pass first).

using System.Globalization;
using System.IO;
using BluePrint.Core.Models;
using BluePrint.DataSources;
using Microsoft.Data.Sqlite;

namespace ReceiptPrinting;

/// <summary>One entry of the Documents list: a name and the retrieval arguments that select it.</summary>
/// <param name="Name">Shown in the list and in the title bar.</param>
/// <param name="Group"><see cref="SampleDocuments.RealReceipts"/> or <see cref="SampleDocuments.LongDocuments"/>.</param>
/// <param name="FromOrder">First order number (Orders.OrderID).</param>
/// <param name="ToOrder">Last order number.</param>
/// <param name="Lines">Most lines to read - exact for every document here.</param>
public sealed record SampleDocument(string Name, string Group, long FromOrder, long ToOrder, long Lines)
{
    /// <summary>The three retrieval arguments of the sample forms, by name.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["FromOrder"] = FromOrder,
        ["ToOrder"] = ToOrder,
        ["Lines"] = Lines,
    };
}

/// <summary>What the Documents list shows of a document before any form reads it.</summary>
/// <param name="Lines">Lines the document has.</param>
/// <param name="Total">Sum of the line amounts after discount.</param>
/// <param name="Customer">Customer of the first order.</param>
/// <param name="OrderDay">Date of the first order, yyyy-MM-dd.</param>
public sealed record DocumentSummary(long Lines, decimal Total, string Customer, string OrderDay);

public static class SampleDocuments
{
    public const string RealReceipts = "Real receipts";
    public const string LongDocuments = "Long documents";

    /// <summary>
    /// The SQL every sample form reads with - the same text as their dataSource.sql. The {bp:...}
    /// functions make it run on every database BluePrint supports; Shippers has three rows
    /// everywhere, which is what lets the 5,000-line document repeat the data.
    /// </summary>
    public const string Sql = """
        SELECT o.OrderID AS OrderNo, {bp:DateText(o.OrderDate)} AS OrderDay,
               c.CompanyName AS Customer, {bp:Concat(e.FirstName, ' ', e.LastName)} AS Salesperson,
               k.ShipperID AS Pass,
               p.ProductName AS ItemName, od.Quantity AS Qty, od.UnitPrice AS Price, od.Discount AS DiscountRate
        FROM Orders o
        JOIN Customers c ON c.CustomerID = o.CustomerID
        JOIN Employees e ON e.EmployeeID = o.EmployeeID
        JOIN {bp:Name(Order Details)} od ON od.OrderID = o.OrderID
        JOIN Products p ON p.ProductID = od.ProductID
        CROSS JOIN Shippers k
        WHERE o.OrderID >= @FromOrder AND o.OrderID <= @ToOrder
        ORDER BY Pass, OrderNo, ItemName
        {bp:Limit(@Lines)}
        """;

    /// <summary>The order ranges add up to the line counts: the lines of Northwind counted in order of OrderID.</summary>
    public static IReadOnlyList<SampleDocument> All { get; } =
    [
        new("Order 10248", RealReceipts, 10248, 10248, 3),
        new("Order 10273", RealReceipts, 10273, 10273, 5),
        new("Order 10657", RealReceipts, 10657, 10657, 6),
        new("Batch 50 lines", LongDocuments, 10248, 10265, 50),
        new("Batch 100 lines", LongDocuments, 10248, 10285, 100),
        new("Batch 1,000 lines", LongDocuments, 10248, 10625, 1000),
        new("Batch 5,000 lines", LongDocuments, 10248, 11077, 5000),
    ];

    /// <summary>northwind.db next to the executable (copied there by the project file).</summary>
    public static string DatabasePath => Path.Combine(AppContext.BaseDirectory, "northwind.db");

    /// <summary>The sample database is only read: Mode=ReadOnly, so nothing can change the file by mistake.</summary>
    public static string ConnectionString(string? databasePath = null) => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath ?? DatabasePath,
        Mode = SqliteOpenMode.ReadOnly,
    }.ToString();

    /// <summary>
    /// Lines, total and first customer of every document, read straight from the database with
    /// <see cref="Sql"/> - so the list shows the same numbers the forms print.
    /// </summary>
    /// <exception cref="SqliteException">The database cannot be opened or read.</exception>
    public static async Task<IReadOnlyDictionary<string, DocumentSummary>> SummarizeAsync(
        string? databasePath = null, CancellationToken ct = default)
    {
        // The application's own query, so translate {bp:...} for SQLite here; a report
        // does this itself when it retrieves.
        var select = BpSql.Translate(Sql, DatabaseProviderType.SQLite);
        var result = new Dictionary<string, DocumentSummary>();

        await using var conn = new SqliteConnection(ConnectionString(databasePath));
        await conn.OpenAsync(ct);
        foreach (var document in All)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), SUM(Price * Qty * (1 - DiscountRate)), MIN(Customer), MIN(OrderDay) " +
                              $"FROM ({select}) AS t";
            foreach (var (name, value) in document.Arguments) cmd.Parameters.AddWithValue("@" + name, value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) continue;
            result[document.Name] = new DocumentSummary(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? 0m : Math.Round(Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture), 2),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3));
        }
        return result;
    }

    /// <summary>
    /// The arguments of <paramref name="document"/> that <paramref name="form"/> declares. BluePrint refuses an
    /// argument a report does not have, so a form of your own that takes only FromOrder still works.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ArgumentsFor(SampleDocument document, ReportDefinition form)
    {
        var declared = new HashSet<string>(
            form.DataSource?.Arguments?.Select(a => a.Name) ?? [], StringComparer.OrdinalIgnoreCase);
        return document.Arguments
            .Where(a => declared.Contains(a.Key))
            .ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase);
    }
}
