// The Reports list: every .bpt file in the Samples folder. A report can be sent
// for a month when it takes the two retrieval arguments FromDate and ToDate - the
// five samples do, and so can a report of your own put in the same folder.

using System.IO;
using BluePrint.Core.Models;
using BluePrint.Render;

namespace SharingHtmlReports.Reports;

public sealed class ReportEntry
{
    public required string Path { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The name of the report (its metadata), or the file name when it has none.</summary>
    public required string Name { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Why the report cannot be retrieved for a month, or null when it can.</summary>
    public string? Problem { get; init; }

    public bool CanRetrieve => Problem is null;
    public string ToolTipText => Problem is null ? Path : $"{Path}\n{Problem}";
}

public static class ReportCatalog
{
    public const string FromArgument = "FromDate";
    public const string ToArgument = "ToDate";

    /// <summary>The reports in <paramref name="folder"/>, by file name. A missing folder gives none.</summary>
    public static IReadOnlyList<ReportEntry> Read(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder, "*.bpt")
            .OrderBy(p => System.IO.Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(ReadOne)
            .ToList();
    }

    public static ReportEntry ReadOne(string path)
    {
        try
        {
            var definition = BluePrintDocument.Load(path).Definition;
            var name = string.IsNullOrWhiteSpace(definition.Metadata.Name)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : definition.Metadata.Name;
            return new ReportEntry
            {
                Path = path,
                Name = name,
                Description = definition.Metadata.Description,
                Problem = TakesMonthArguments(definition)
                    ? null
                    : $"This report does not take the retrieval arguments {FromArgument} and {ToArgument}.",
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ReportEntry
            {
                Path = path,
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Problem = $"This report cannot be opened: {ex.Message}",
            };
        }
    }

    /// <summary>The report reads a database and has a FromDate and a ToDate argument (any letter case).</summary>
    public static bool TakesMonthArguments(ReportDefinition definition)
    {
        var arguments = definition.DataSource?.Arguments;
        if (definition.DataSource?.DataSourceType != DataSourceType.DBSQL || arguments is null) return false;
        bool Has(string name) => arguments.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        return Has(FromArgument) && Has(ToArgument);
    }
}
