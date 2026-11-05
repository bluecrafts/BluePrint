// Translations from the CSV file the Designer exports (Translations > Export CSV...)
// after a translator filled in the language columns. The application reads the
// file itself and gives it to the report as one more layer of translations; the
// report file is not changed and nothing is imported back into it.
//
// The same interface takes translations from anywhere: a table of your database
// or a translation service works the same way.

using System.IO;
using System.Text;
using BluePrint.Render.Localization;

namespace LocalizationShowcase.Translations;

/// <summary>
/// An <see cref="IReportStringSource"/> over a translations CSV: a header row
/// <c>key,kind,&lt;source&gt;,&lt;language&gt;...</c> and one row for every text.
/// An empty cell means "not translated", so the report falls back to the next layer.
/// </summary>
public sealed class CsvStringSource : IReportStringSource
{
    private readonly Dictionary<string, Dictionary<string, string>> _byLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads <paramref name="path"/> (UTF-8, with or without a byte order mark).</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="FormatException">The file has no header row with a key column and at least one language.</exception>
    public CsvStringSource(string path) : this(File.ReadAllText(path, Encoding.UTF8), path) { }

    /// <summary>Reads CSV content that is already in memory. <paramref name="name"/> is used in error messages.</summary>
    /// <exception cref="FormatException">The content has no header row with a key column and at least one language.</exception>
    public CsvStringSource(string content, string name)
    {
        var rows = Parse(content.TrimStart((char)0xFEFF));
        if (rows.Count == 0 || rows[0].Count < 3 || !rows[0][0].Equals("key", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"{name}: the first row must be the header key,kind,<language>,...");

        var header = rows[0];
        Languages = [.. header.Skip(2).Select(h => h.Trim())];
        foreach (var lang in Languages) _byLanguage[lang] = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var row in rows.Skip(1))
        {
            if (row.Count == 0 || row[0].Length == 0) continue;
            for (int column = 2; column < header.Count && column < row.Count; column++)
                if (row[column].Length > 0) _byLanguage[header[column].Trim()][row[0]] = row[column];
        }
    }

    /// <summary>The language columns of the file, the source language first (as the Designer writes them).</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>
    /// The text of <paramref name="key"/> in exactly <paramref name="language"/>. The report asks for the
    /// parent language (de-CH, then de) itself, so this does not.
    /// </summary>
    public bool TryGet(string key, string language, out string text)
    {
        text = "";
        return _byLanguage.TryGetValue(language, out var texts) && texts.TryGetValue(key, out text!);
    }

    // RFC 4180: fields separated by commas, a quoted field can hold commas, quotes ("") and line breaks.
    private static List<List<string>> Parse(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
                continue;
            }

            switch (c)
            {
                case '"': quoted = true; any = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); any = true; break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = []; any = false;
                    break;
                default: field.Append(c); any = true; break;
            }
        }
        if (any || field.Length > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
