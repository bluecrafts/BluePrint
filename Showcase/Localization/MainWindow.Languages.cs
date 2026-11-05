// The Language list and the language buttons of the toolbar: one choice in two
// places. Choosing a language renders the open document again in it - the data
// is not read again.
//
// Each row shows the culture the form gives the language (localization.cultures),
// whether the form declares it, and which language the form is written in.

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BluePrint.Core.Models;

namespace LocalizationShowcase;

/// <summary>One row of the Language list, for the form that is open.</summary>
public sealed class LanguageRow(AppLanguage language) : INotifyPropertyChanged
{
    public AppLanguage Language { get; } = language;
    public string Tag => Language.Tag;
    public string Name => Language.Name;

    /// <summary>"ja - ja-JP", or "ja - report culture" when the form gives the language no culture.</summary>
    public string Detail { get; private set; } = $"{language.Tag} - report culture";

    /// <summary>The form is written in this language: no translation is looked up.</summary>
    public bool IsSource { get; private set; }

    /// <summary>The form lists this language in localization.languages.</summary>
    public bool IsDeclared { get; private set; } = true;

    /// <summary>The culture the dates and numbers follow, or null for the report's own.</summary>
    public string? Culture { get; private set; }

    /// <summary>Texts that stay in the source language when the form is shown in this language.</summary>
    public int Missing { get; private set; }
    public bool HasMissing => Missing > 0 && IsDeclared;

    public string ToolTipText => !IsDeclared
        ? $"This form does not declare {Name}. The source text is shown."
        : Missing > 0 ? $"{Missing} texts are not translated."
        : $"{Name} ({Tag})";

    public void ShowMissing(LocalizationShowcase.Translations.SourceReport report)
    {
        Missing = report.Missing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void ShowForm(LocalizationDefinition? localization)
    {
        Culture = localization?.Cultures?.FirstOrDefault(c => string.Equals(c.Key, Tag, StringComparison.OrdinalIgnoreCase)).Value;
        Detail = $"{Tag} - {Culture ?? "report culture"}";
        IsSource = string.Equals(localization?.SourceLanguage ?? Languages.Default, Tag, StringComparison.OrdinalIgnoreCase);
        IsDeclared = IsSource || localization?.Languages?.Contains(Tag, StringComparer.OrdinalIgnoreCase) == true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));   // every property
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // The name screen readers and UI Automation give the row.
    public override string ToString() => Name;
}

public partial class MainWindow
{
    private readonly List<LanguageRow> _languages = [.. Languages.All.Select(l => new LanguageRow(l))];

    private LanguageRow CurrentLanguageRow => LanguagesList.SelectedItem as LanguageRow ?? _languages[0];
    private AppLanguage CurrentLanguage => CurrentLanguageRow.Language;

    private void InitializeLanguages()
    {
        LanguagesList.ItemsSource = _languages;
        foreach (var row in _languages)
        {
            // ASCII tags on the buttons: they show in every font.
            var button = new ToggleButton
            {
                Content = row.Tag,
                Tag = row,
                MinWidth = 32,
                Margin = new Thickness(0, 0, 2, 0),
                ToolTip = $"{row.Name} ({row.Tag})",
            };
            AutomationProperties.SetName(button, row.Name);
            // Checked, not Click: UI Automation (a screen reader) toggles the button without a click.
            // Pressing the button of the language shown again leaves it pressed.
            button.Checked += (_, _) => { if (!_showingButtons) SelectLanguage(row.Tag); };
            button.Unchecked += (_, _) => { if (!_showingButtons) ShowLanguageButtons(); };
            LanguageButtons.Children.Add(button);
        }
        LanguagesList.SelectedIndex = 0;
    }

    private void SelectLanguage(string? tag)
    {
        var row = _languages.FirstOrDefault(l => string.Equals(l.Tag, tag, StringComparison.OrdinalIgnoreCase)) ?? _languages[0];
        if (ReferenceEquals(LanguagesList.SelectedItem, row)) ShowLanguageButtons();   // a button pressed again stays pressed
        else LanguagesList.SelectedItem = row;
    }

    private void LanguagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagesList.SelectedItem is not LanguageRow row) return;
        ShowLanguageButtons();
        ShowSources();   // the panel, the counts of the language rows and the status bar
        UpdateTitle();
        if (_config.LastLanguage != row.Tag)
        {
            _config.LastLanguage = row.Tag;
            SaveConfig();
        }
        ScheduleLanguageRender();
    }

    // Setting the buttons from the list raises their Checked / Unchecked: that must not choose again.
    private bool _showingButtons;

    private void ShowLanguageButtons()
    {
        _showingButtons = true;
        try
        {
            foreach (var button in LanguageButtons.Children.OfType<ToggleButton>())
                button.IsChecked = ReferenceEquals(button.Tag, LanguagesList.SelectedItem);
        }
        finally { _showingButtons = false; }
    }

    // The rows follow the form that is open: its cultures, the languages it declares, its source language.
    private void ShowFormLanguages()
    {
        var localization = _openForm?.Document.Definition.Localization;
        foreach (var row in _languages) row.ShowForm(localization);
        ShowLanguageStatus();
    }

    private void ShowLanguageStatus()
    {
        var row = CurrentLanguageRow;
        var text = $"{row.Name} ({row.Tag}) - culture {row.Culture ?? "of the report"}";
        if (!row.IsDeclared) text += " - not declared by this form, the source text is shown";
        else if (row.IsSource) text += " - the source language of this form";
        if (SelectedForm?.Name == "Invoice-PerCustomer" && SelectedDocument?.IsBatch == true)
            text += " - customer language overrides the selection for German-speaking customers";
        text += SourcesStatus();
        StatusLanguageText.Text = text;
    }

    private void StepLanguage(int step)
    {
        int index = _languages.IndexOf(CurrentLanguageRow);
        SelectLanguage(_languages[(index + step + _languages.Count) % _languages.Count].Tag);
    }

    private void PreviousLanguage_Executed(object sender, ExecutedRoutedEventArgs e) => StepLanguage(-1);
    private void NextLanguage_Executed(object sender, ExecutedRoutedEventArgs e) => StepLanguage(+1);

    private void SelectLanguage_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string text && int.TryParse(text, out int index) && index >= 0 && index < _languages.Count)
            SelectLanguage(_languages[index].Tag);
    }
}
