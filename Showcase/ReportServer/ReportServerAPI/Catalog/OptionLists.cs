// The values the web page offers for the list conditions, read once from the
// database at startup (the database is opened read-only and never changes).
//
// These queries are the server's own, not a report's, so they are written for
// SQLite only. The reports themselves use portable SQL ({bp:...}) and run on any
// database BluePrint supports.

using Microsoft.Data.Sqlite;

namespace ReportServerApi.Catalog;

public sealed record OptionLists(
    IReadOnlyList<ParameterOption> Countries,
    IReadOnlyList<ParameterOption> Employees,
    IReadOnlyList<ParameterOption> Categories,
    IReadOnlyList<ParameterOption> Shippers,
    IReadOnlyList<ParameterOption> Customers,
    string FirstDay,
    string LastDay)
{
    /// <summary>Read-only: the server never changes the database.</summary>
    public static string ConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadOnly,
    }.ToString();

    public static OptionLists Read(string databasePath)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException($"The Northwind database is not there: {databasePath}", databasePath);

        using var connection = new SqliteConnection(ConnectionString(databasePath));
        connection.Open();

        var countries = Query(connection, "SELECT DISTINCT Country, Country FROM Customers ORDER BY Country");
        var employees = Query(connection, "SELECT EmployeeID, LastName || ', ' || FirstName AS Name FROM Employees ORDER BY Name");
        var categories = Query(connection, "SELECT CategoryID, CategoryName FROM Categories ORDER BY CategoryName");
        var shippers = Query(connection, "SELECT ShipperID, CompanyName FROM Shippers ORDER BY CompanyName");
        var customers = Query(connection, "SELECT CustomerID, CompanyName FROM Customers ORDER BY CompanyName");
        var range = Query(connection, "SELECT MIN(substr(OrderDate, 1, 10)), MAX(substr(OrderDate, 1, 10)) FROM Orders");

        return new OptionLists(
            [new(ReportCatalog.AllText, "All countries"), .. countries],
            [new(ReportCatalog.AllNumber, "All employees"), .. employees],
            [new(ReportCatalog.AllNumber, "All categories"), .. categories],
            [new(ReportCatalog.AllNumber, "All shippers"), .. shippers],
            [new(ReportCatalog.AllText, "All customers"), .. customers],
            range[0].Value,
            range[0].Label);
    }

    // Two columns: the value sent back, and the text shown.
    private static List<ParameterOption> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<ParameterOption>();
        while (reader.Read())
            list.Add(new ParameterOption(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                                         reader.GetString(1)));
        return list;
    }
}
