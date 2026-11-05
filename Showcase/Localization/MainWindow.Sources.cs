// The Translation sources panel: the layers of the form, in the order they win,
// with the number of texts each one gives in the language shown - then the
// translations embedded in the report, then the texts that stay in the source
// language. Double-click a row to open its file.

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BluePrint.UI.Controls;
using LocalizationShowcase.Translations;

namespace LocalizationShowcase;

/// <summary>One row of the Translation sources panel.</summary>
public sealed class SourceRow
{
    public string Number { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>The languages of the layer, "(ja)", or why it is not used.</summary>
    public string Detail { get; init; } = "";

    /// <summary>Texts this layer gives in the language shown; "-" when it has none in that language.</summary>
    public string CountText { get; init; } = "";

    /// <summary>Shown in the secondary colour: the layer gives nothing in this language.</summary>
    public bool IsDim { get; init; }

    /// <summary>Warning (texts in the source language) or Error (a file that could not be read).</summary>
    public BpMessageKind? Problem { get; init; }
    public bool HasProblem => Problem is not null;
    public BpMessageKind ProblemKind => Problem ?? BpMessageKind.Information;

    public string? ToolTipText { get; init; }

    /// <summary>The file a double click opens.</summary>
    public string? Path { get; init; }

    // The name screen readers and UI Automation give the row.
    public override string ToString() => $"{Name} {Detail} {CountText}".Trim();
}

public partial class MainWindow
{
    private SourceReport? _sources;

    // The panel and everything that counts the texts of a language: the rows of the Language list and the status bar.
    private void ShowSources()
    {
        if (_openForm is null)
        {
            SourcesList.ItemsSource = null;
            SourcesSummaryText.Text = "";
            ShowFontWarning();
            return;
        }

        foreach (var row in _languages) row.ShowMissing(_openForm.Inspect(row.Tag));

        var language = CurrentLanguage;
        var report = _sources = _openForm.Inspect(language.Tag);
        var rows = new List<SourceRow>();
        bool lookedUp = !report.IsSourceLanguage;

        for (int i = 0; i < report.Layers.Count; i++)
        {
            var use = report.Layers[i];
            var layer = use.Layer;
            bool hasLanguage = layer.Languages.Any(l => SameLanguage(l, language.Tag));
            rows.Add(new SourceRow
            {
                Number = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = layer.Name,
                Detail = layer.Error is not null ? layer.Error
                       : use.OnlySourceLanguage ? "(not used: same as the source language)"
                       : $"({string.Join(", ", layer.Languages)})",
                CountText = layer.Error is not null || !lookedUp || !hasLanguage ? "-" : use.Wins.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                IsDim = layer.Error is null && (!lookedUp || !hasLanguage || use.OnlySourceLanguage),
                Problem = layer.Error is not null ? BpMessageKind.Error : null,
                ToolTipText = layer.Error is not null ? $"{layer.Path}\n{layer.Error}"
                            : use.Replaces.Count > 0 ? $"Replaces {use.Replaces.Count} texts from lower layers: {string.Join(", ", use.Replaces)}"
                            : layer.Path,
                Path = layer.Path,
            });
        }

        bool reportHasLanguage = report.ReportLanguages.Any(l => SameLanguage(l, language.Tag));
        rows.Add(new SourceRow
        {
            Number = (report.Layers.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = "Embedded in the report",
            Detail = report.ReportLanguages.Count > 0 ? $"({string.Join(", ", report.ReportLanguages)})" : "(no translations)",
            CountText = lookedUp && reportHasLanguage ? report.ReportWins.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-",
            IsDim = !lookedUp || !reportHasLanguage,
            ToolTipText = _openForm.Entry.Path,
            Path = _openForm.Entry.Path,
        });

        int sourceCount = report.IsSourceLanguage ? report.Total : report.Missing;
        rows.Add(new SourceRow
        {
            Name = $"Source text ({report.SourceLanguage})",
            CountText = sourceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IsDim = report.IsSourceLanguage,
            Problem = report.Missing > 0 ? BpMessageKind.Warning : null,
            ToolTipText = report.Missing > 0
                ? "Not translated: " + string.Join(", ", report.Texts.Where(t => t.Kind == BluePrint.Render.Localization.TextSourceKind.SourceText).Select(t => t.Key))
                : "The texts written in the report itself",
            Path = _openForm.Entry.Path,
        });

        SourcesList.ItemsSource = rows;
        SourcesSummaryText.Text = report.IsSourceLanguage
            ? $"{language.Name} is the source language. No translation is looked up."
            : report.Missing == 0
                ? $"{language.Name}: all {report.Total} texts are translated."
                : $"{language.Name}: {report.Missing} of {report.Total} texts show the source text.";
        ShowLanguageStatus();
        ShowFontWarning();
    }

    /// <summary>The end of the status bar: "all 24 texts translated" or "2 texts show the source text".</summary>
    private string SourcesStatus() => _sources is not { IsSourceLanguage: false } report ? ""
        : report.Missing == 0 ? $" - all {report.Total} texts translated"
        : $" - {report.Missing} texts show the source text";

    private static bool SameLanguage(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // A double click opens the file with the program Windows has for it: a JSON or CSV file
    // in an editor, the report itself in BluePrint Studio when it is installed.
    private void SourcesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SourcesList.SelectedItem is not SourceRow { Path: { } path }) return;
        if (!File.Exists(path))
        {
            SetStatus($"File not found: {path}", BpMessageKind.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus($"Could not open {Path.GetFileName(path)}: {ex.Message}", BpMessageKind.Warning);
        }
    }

    private void SourcesExpander_Changed(object sender, RoutedEventArgs e)
    {
        if (_config.SourcesExpanded == SourcesExpander.IsExpanded) return;
        _config.SourcesExpanded = SourcesExpander.IsExpanded;
        SaveConfig();
    }
}
