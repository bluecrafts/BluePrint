// Turns the query string of a request into the retrieval arguments of a report.
//
// Every condition of the report may be given by its name (?FromDate=1997-01-01);
// one that is left out takes its default. A value is checked before it gets near
// the database: a date has to be yyyy-MM-dd inside the range of the data, and a
// list value has to be one of the values of the list. The SQL of the reports binds
// every value as a parameter in any case - nothing from the request is ever pasted
// into SQL text.
//
// The checked values also make the cache key: the same report with the same
// conditions is rendered once, whoever asks and in whatever order the query string
// lists them.

using System.Globalization;

namespace ReportServerApi.Catalog;

public sealed class BadRequestException(string message) : Exception(message);

public sealed record ReportArguments(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyDictionary<string, string> Texts,
    string Key)
{
    /// <summary>Query string names that are not conditions: they belong to the endpoints.</summary>
    public static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "layout", "pages" };

    public static ReportArguments From(ReportEntry report, IQueryCollection query)
    {
        foreach (var name in query.Keys)
        {
            if (Reserved.Contains(name)) continue;
            if (!report.Parameters.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new BadRequestException(
                    $"'{name}' is not a condition of the report {report.Id}. " +
                    $"Its conditions are: {string.Join(", ", report.Parameters.Select(p => p.Name))}.");
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in report.Parameters)
        {
            string text = query.TryGetValue(p.Name, out var given) && !string.IsNullOrWhiteSpace(given.ToString())
                ? given.ToString().Trim()
                : p.DefaultValue;

            values[p.Name] = p.Kind switch
            {
                ParameterKind.Date => ParseDate(p, text),
                _ => ParseOption(p, text),
            };
            texts[p.Name] = text;
        }

        if (values.TryGetValue("FromDate", out var from) && values.TryGetValue("ToDate", out var to)
            && from is DateTime f && to is DateTime t && f > t)
            throw new BadRequestException($"The period starts after it ends: {texts["FromDate"]} to {texts["ToDate"]}.");

        string key = string.Join(";", report.Parameters.Select(p => $"{p.Name}={texts[p.Name]}"));
        return new ReportArguments(values, texts, key);
    }

    private static DateTime ParseDate(ParameterDefinition p, string text)
    {
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BadRequestException($"{p.Name}: '{text}' is not a date written as yyyy-MM-dd.");
        // The range is advisory for the page; the server only refuses what no report
        // could use, so a period may reach past the first or the last order.
        if (date.Year is < 1900 or > 2100)
            throw new BadRequestException($"{p.Name}: {text} is out of range.");
        return date;
    }

    private static object ParseOption(ParameterDefinition p, string text)
    {
        var option = p.Options?.FirstOrDefault(o => o.Value.Equals(text, StringComparison.OrdinalIgnoreCase))
            ?? throw new BadRequestException($"{p.Name}: '{text}' is not one of the values of the list.");

        // A list of numbers (an ID) is sent to the report as a number, the others as text.
        return long.TryParse(option.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
            ? number
            : option.Value;
    }
}
