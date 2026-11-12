// The Month list: the months that have orders in the Northwind database, newest
// first, and the values of the FromDate and ToDate arguments for a month.
//
// This query is the application's own, not a report's, so it is written for SQLite
// only. The reports themselves use portable SQL ({bp:...}) and run on any database
// BluePrint supports.

using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SharingHtmlReports.Reports;

public sealed record MonthEntry(int Year, int Month, int Orders, bool IsPartial)
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public DateTime FromDate => new(Year, Month, 1);
    public DateTime ToDate => FromDate.AddMonths(1).AddDays(-1);

    /// <summary>"1998-04": how the month is kept in the settings.</summary>
    public string Key => FromDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>"April 1998".</summary>
    public string Name => FromDate.ToString("MMMM yyyy", English);

    public string YearText => Year.ToString(CultureInfo.InvariantCulture);

    public string OrdersText => (Orders == 1 ? "1 order" : $"{Orders} orders") + (IsPartial ? " (partial)" : "");

    public string ToolTipText =>
        $"{OrdersText}, {FromDate.ToString("d MMMM yyyy", English)} to {ToDate.ToString("d MMMM yyyy", English)}" +
        (IsPartial ? "\nThe database ends in this month: it has orders for the first days only." : "");

    /// <summary>The retrieval arguments of the month: the first and the last day.</summary>
    public IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?>
    {
        [ReportCatalog.FromArgument] = FromDate,
        [ReportCatalog.ToArgument] = ToDate,
    };
}

public static class MonthCatalog
{
    /// <summary>The Northwind database next to the executable.</summary>
    public static string DatabasePath => Path.Combine(AppContext.BaseDirectory, "northwind.db");

    /// <summary>Read-only: the application never changes the database.</summary>
    public static string ConnectionString(string? path = null) => new SqliteConnectionStringBuilder
    {
        DataSource = path ?? DatabasePath,
        Mode = SqliteOpenMode.ReadOnly,
    }.ToString();

    /// <summary>The months with orders, newest first.</summary>
    /// <exception cref="SqliteException">The file is not a Northwind database.</exception>
    public static IReadOnlyList<MonthEntry> Read(string? path = null)
    {
        using var connection = new SqliteConnection(ConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT substr(OrderDate, 1, 7) AS Ym, COUNT(*) AS Orders, MAX(CAST(substr(OrderDate, 9, 2) AS INTEGER)) AS LastDay " +
            "FROM Orders GROUP BY Ym ORDER BY Ym DESC";
        using var reader = command.ExecuteReader();

        var months = new List<MonthEntry>();
        while (reader.Read())
        {
            if (!DateTime.TryParseExact(reader.GetString(0), "yyyy-MM", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out var month))
                continue;
            int orders = reader.GetInt32(1);
            int lastDay = reader.GetInt32(2);
            // The newest month of the data stops early (Northwind ends on 6 May 1998).
            bool partial = months.Count == 0 && lastDay <= DateTime.DaysInMonth(month.Year, month.Month) - 7;
            months.Add(new MonthEntry(month.Year, month.Month, orders, partial));
        }
        return months;
    }

    /// <summary>The month to show first: the newest one that is not partial.</summary>
    public static MonthEntry? DefaultMonth(IReadOnlyList<MonthEntry> months) =>
        months.FirstOrDefault(m => !m.IsPartial) ?? months.FirstOrDefault();
}
