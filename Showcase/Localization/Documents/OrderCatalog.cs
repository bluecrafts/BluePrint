// The documents of the Documents list: each one is a range of orders of the
// Northwind database of this repository, given to the forms as the retrieval
// arguments FromOrder and ToOrder.
//
// A batch is several invoices in one render: every order starts a new page and
// numbers its own pages. The 10-invoice batch has German and Swiss customers
// between French, Brazilian, Belgian and Venezuelan ones, which is what the
// Invoice-PerCustomer form needs to show each invoice in its customer's language.

using System.Globalization;
using System.IO;
using BluePrint.Core.Models;
using BluePrint.DataSources;
using Microsoft.Data.Sqlite;

namespace LocalizationShowcase.Documents;

/// <summary>One entry of the Documents list: a name and the range of orders it prints.</summary>
/// <param name="Name">Shown in the list and in the status bar.</param>
/// <param name="Group"><see cref="OrderCatalog.Batches"/> or <see cref="OrderCatalog.Orders"/>.</param>
/// <param name="FromOrder">First order number (Orders.OrderID).</param>
/// <param name="ToOrder">Last order number.</param>
public sealed record OrderDocument(string Name, string Group, long FromOrder, long ToOrder)
{
    /// <summary>The two retrieval arguments of the sample forms, by name.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["FromOrder"] = FromOrder,
        ["ToOrder"] = ToOrder,
    };
}

/// <summary>One order as the Documents list shows it, before any form reads it.</summary>
/// <param name="OrderId">Orders.OrderID.</param>
/// <param name="OrderDay">Date of the order, yyyy-MM-dd.</param>
/// <param name="Customer">Company name of the customer.</param>
/// <param name="Country">Country of the customer.</param>
/// <param name="Lines">Lines of the order.</param>
/// <param name="Total">Lines after discount plus freight: the total the invoice prints.</param>
public sealed record OrderSummary(long OrderId, string OrderDay, string Customer, string Country, long Lines, decimal Total);

public static class OrderCatalog
{
    public const string Batches = "Batches";
    public const string Orders = "Orders";

    /// <summary>
    /// The SQL every sample form reads with - the same text as their dataSource.sql. The {bp:...}
    /// functions make it run on every database BluePrint supports.
    /// </summary>
    public const string Sql = """
        SELECT o.OrderID AS OrderNo, {bp:DateText(o.OrderDate)} AS OrderedOn, o.Freight AS FreightCost,
               c.CompanyName AS Customer, c.Address AS CustAddress, c.City AS CustCity,
               c.PostalCode AS CustPostalCode, c.Country AS CustCountry,
               s.CompanyName AS ShipperName, {bp:Concat(e.FirstName, ' ', e.LastName)} AS Salesperson,
               p.ProductName AS ItemName, od.Quantity AS Qty, od.UnitPrice AS Price, od.Discount AS DiscountRate
        FROM Orders o
        JOIN Customers c ON c.CustomerID = o.CustomerID
        JOIN Employees e ON e.EmployeeID = o.EmployeeID
        JOIN Shippers  s ON s.ShipperID  = o.ShipVia
        JOIN {bp:Name(Order Details)} od ON od.OrderID = o.OrderID
        JOIN Products p ON p.ProductID = od.ProductID
        WHERE o.OrderID >= @FromOrder AND o.OrderID <= @ToOrder
        ORDER BY OrderNo, ItemName
        """;

    /// <summary>The batches at the top of the Documents list.</summary>
    public static IReadOnlyList<OrderDocument> BatchDocuments { get; } =
    [
        new("Orders 10248-10257 (10 invoices)", Batches, 10248, 10257),
        new("Orders 10248-10347 (100 invoices)", Batches, 10248, 10347),
    ];

    /// <summary>The batches, then one document for every order.</summary>
    public static IReadOnlyList<OrderDocument> Documents(IEnumerable<OrderSummary> orders) =>
        [.. BatchDocuments, .. orders.Select(o => new OrderDocument(o.OrderId.ToString(CultureInfo.InvariantCulture), Orders, o.OrderId, o.OrderId))];

    /// <summary>northwind.db next to the executable (copied there by the project file).</summary>
    public static string DatabasePath => Path.Combine(AppContext.BaseDirectory, "northwind.db");

    /// <summary>The sample database is only read: Mode=ReadOnly, so nothing can change the file by mistake.</summary>
    public static string ConnectionString(string? databasePath = null) => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath ?? DatabasePath,
        Mode = SqliteOpenMode.ReadOnly,
    }.ToString();

    /// <summary>
    /// Every order of the database, read with <see cref="Sql"/> - so the list shows the same
    /// customers, lines and totals the invoices print.
    /// </summary>
    /// <exception cref="SqliteException">The database cannot be opened or read.</exception>
    public static async Task<IReadOnlyList<OrderSummary>> ReadOrdersAsync(string? databasePath = null, CancellationToken ct = default)
    {
        // The application's own query, so translate {bp:...} for SQLite here; a report
        // does this itself when it retrieves.
        var select = BpSql.Translate(Sql, DatabaseProviderType.SQLite);

        await using var conn = new SqliteConnection(ConnectionString(databasePath));
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT OrderNo, MIN(OrderedOn), MIN(Customer), MIN(CustCountry), COUNT(*), " +
                          "SUM(Price * Qty * (1 - DiscountRate)), MIN(FreightCost) " +
                          $"FROM ({select}) AS t GROUP BY OrderNo ORDER BY OrderNo";
        cmd.Parameters.AddWithValue("@FromOrder", long.MinValue);
        cmd.Parameters.AddWithValue("@ToOrder", long.MaxValue);

        var result = new List<OrderSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var lines = Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture);
            var freight = Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture);
            result.Add(new OrderSummary(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.GetInt64(4),
                Math.Round(lines + freight, 2)));
        }
        return result;
    }

    /// <summary>
    /// The arguments of <paramref name="document"/> that <paramref name="form"/> declares. BluePrint refuses an
    /// argument a report does not have, so a form of your own that takes only FromOrder still works.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ArgumentsFor(OrderDocument document, ReportDefinition form)
    {
        var declared = new HashSet<string>(
            form.DataSource?.Arguments?.Select(a => a.Name) ?? [], StringComparer.OrdinalIgnoreCase);
        return document.Arguments
            .Where(a => declared.Contains(a.Key))
            .ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase);
    }
}
