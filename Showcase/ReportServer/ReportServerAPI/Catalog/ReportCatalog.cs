// The six reports the server offers, and the conditions each one takes.
//
// A condition is a retrieval argument of the report (dataSource.arguments in the
// .bpt): the SQL of the report reads it as @Name. This file adds what the .bpt does
// not know - the label shown in the web page, the default value, and the list of
// values to choose from. At startup every report is opened and its arguments are
// compared with the conditions declared here, so the two cannot drift apart: a
// condition the report does not read, or an argument with no condition, stops the
// server with a message that names both.

using BluePrint.Render;
using Microsoft.Extensions.Options;

namespace ReportServerApi.Catalog;

/// <summary>What the web page needs to show a condition and send its value.</summary>
public sealed record ParameterDefinition(
    string Name,
    string Label,
    ParameterKind Kind,
    string DefaultValue,
    string? Min = null,
    string? Max = null,
    IReadOnlyList<ParameterOption>? Options = null);

public enum ParameterKind { Date, Select }

public sealed record ParameterOption(string Value, string Label);

/// <summary>One report: its file, its texts and its conditions.</summary>
public sealed record ReportEntry(
    string Id,
    string FilePath,
    string Title,
    string Description,
    string Orientation,
    IReadOnlyList<ParameterDefinition> Parameters);

public sealed class ReportCatalog
{
    // The value of a list condition that means "no filter". The SQL of the reports
    // tests for it: (@Country = '*' OR c.Country = @Country).
    public const string AllText = "*";
    public const string AllNumber = "0";

    private readonly Dictionary<string, ReportEntry> _reports;

    public ReportCatalog(IOptions<ReportServerOptions> options, ILogger<ReportCatalog> log)
    {
        var settings = options.Value;
        string reportDir = ReportServerOptions.Resolve(settings.Reports);
        var lists = OptionLists.Read(ReportServerOptions.Resolve(settings.Database));

        // The period: the first and the last order in the database. Northwind has
        // orders from July 1996 to May 1998; the default is the one full year, 1997.
        ParameterDefinition From(string from = "1997-01-01") => new("FromDate", "From", ParameterKind.Date, from, lists.FirstDay, lists.LastDay);
        ParameterDefinition To(string to = "1997-12-31") => new("ToDate", "To", ParameterKind.Date, to, lists.FirstDay, lists.LastDay);

        (string Id, string File, ParameterDefinition[] Parameters)[] specs =
        [
            ("sales-by-customer", "sales-by-customer.bpt",
                [From(), To(), new("Country", "Customer country", ParameterKind.Select, AllText, Options: lists.Countries)]),
            ("sales-by-employee", "sales-by-employee.bpt",
                [From(), To(), new("EmployeeId", "Employee", ParameterKind.Select, AllNumber, Options: lists.Employees)]),
            ("product-sales", "product-sales.bpt",
                [From(), To(), new("CategoryId", "Category", ParameterKind.Select, AllNumber, Options: lists.Categories)]),
            ("shipping-register", "shipping-register.bpt",
                [From(), To(), new("ShipperId", "Shipper", ParameterKind.Select, AllNumber, Options: lists.Shippers)]),
            ("sales-dashboard", "sales-dashboard.bpt",
                [From(), To(), new("Country", "Customer country", ParameterKind.Select, AllText, Options: lists.Countries)]),
            // One invoice per order, so a year is some 400 pages: the default is one month.
            ("invoices", "invoices.bpt",
                [From("1997-01-01"), To("1997-01-31"), new("CustomerId", "Customer", ParameterKind.Select, AllText, Options: lists.Customers)]),
        ];

        _reports = new(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            string path = Path.Combine(reportDir, spec.File);
            var definition = BluePrintDocument.Load(path).Definition;
            CheckArguments(spec.File, definition.DataSource?.Arguments?.Select(a => a.Name) ?? [], spec.Parameters);

            _reports[spec.Id] = new ReportEntry(
                spec.Id, path,
                definition.Metadata.Name,
                definition.Metadata.Description ?? "",
                definition.Report.Page.Orientation.ToString().ToLowerInvariant(),
                spec.Parameters);
        }

        log.LogInformation("{Count} reports from {Folder}", _reports.Count, reportDir);
    }

    /// <summary>The reports in the order they are listed in the web page.</summary>
    public IEnumerable<ReportEntry> All => _reports.Values;

    public ReportEntry? Find(string id) => _reports.GetValueOrDefault(id);

    private static void CheckArguments(string file, IEnumerable<string> arguments, IEnumerable<ParameterDefinition> parameters)
    {
        var declared = arguments.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var offered = parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notRead = offered.Except(declared).ToList();
        var notOffered = declared.Except(offered).ToList();
        if (notRead.Count == 0 && notOffered.Count == 0) return;

        throw new InvalidOperationException(
            $"{file}: the conditions in ReportCatalog do not match the arguments of the report. " +
            (notRead.Count > 0 ? $"Not an argument of the report: {string.Join(", ", notRead)}. " : "") +
            (notOffered.Count > 0 ? $"An argument with no condition: {string.Join(", ", notOffered)}." : ""));
    }
}
