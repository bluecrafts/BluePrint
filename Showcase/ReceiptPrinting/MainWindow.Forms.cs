// The Forms list: every .bpt file in Samples next to the executable, grouped by
// the paper it is made for, plus any form opened with File > Open Form. A form is
// only read here - the application has no designer and never saves one.

using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BluePrint.Core.Models;
using BluePrint.Core.Serialization;
using BluePrint.UI.Controls;
using Microsoft.Win32;

namespace ReceiptPrinting;

/// <summary>One entry of the Forms list.</summary>
public sealed class FormEntry
{
    public const string Paper80 = "80 mm paper";
    public const string Paper58 = "58 mm paper";
    public const string Other = "Other";

    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>"72 mm - Continuous" or "72 mm - Paged, 200 mm pages"; "(cannot be read)" when the file is not a report.</summary>
    public required string Detail { get; init; }

    /// <summary><see cref="Paper80"/>, <see cref="Paper58"/> or <see cref="Other"/>.</summary>
    public required string Group { get; init; }

    /// <summary>Opened with File > Open Form, not found in Samples.</summary>
    public bool IsExternal { get; init; }

    public bool CanRead { get; init; } = true;

    /// <summary>Order of the groups in the list: 80 mm, 58 mm, then the rest.</summary>
    public int GroupOrder => Group switch { Paper80 => 0, Paper58 => 1, _ => 2 };

    // The name screen readers and UI Automation give the list item.
    public override string ToString() => Name;

    /// <summary>
    /// Reads only what the list shows: the page of the report. The paper a form is for comes
    /// from the width it prints: 64 to 80 mm on 80 mm paper, 40 to 58 mm on 58 mm paper.
    /// </summary>
    public static FormEntry Read(string path, bool external = false)
    {
        try
        {
            var page = BptSerializer.Load(path).Report.Page;
            double w = page.PaperSize.WidthMm;
            string width = w.ToString("0.#", CultureInfo.InvariantCulture) + " mm";
            string detail = page.HeightMode == PageHeightMode.Continuous
                ? $"{width} - Continuous"
                : $"{width} - Paged, {page.PaperSize.HeightMm.ToString("0.#", CultureInfo.InvariantCulture)} mm pages";
            string group = external ? Other : w is >= 64 and <= 80 ? Paper80 : w is >= 40 and <= 58 ? Paper58 : Other;
            return new FormEntry { Path = path, Detail = detail, Group = group, IsExternal = external };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new FormEntry { Path = path, Detail = "(cannot be read)", Group = Other, IsExternal = external, CanRead = false };
        }
    }
}

public partial class MainWindow
{
    private const string DefaultForm = "Receipt-80mm-Continuous";

    private readonly List<FormEntry> _forms = [];

    // Restoring the selection after a form failed to open must not open anything.
    private bool _restoringForm;

    private FormEntry? SelectedForm => FormsList.SelectedItem as FormEntry;

    private void InitializeForms()
    {
        var view = CollectionViewSource.GetDefaultView(_forms);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FormEntry.Group)));
        view.SortDescriptions.Add(new SortDescription(nameof(FormEntry.GroupOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(FormEntry.IsExternal), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(FormEntry.Name), ListSortDirection.Ascending));
        FormsList.ItemsSource = view;
        RefreshForms();
    }

    // Reads the Samples folder again. Forms opened with Open Form stay; the selection stays on the same file.
    private void RefreshForms()
    {
        var selected = SelectedForm?.Path;
        var external = _forms.Where(f => f.IsExternal).ToList();

        _forms.Clear();
        if (Directory.Exists(SamplesFolder))
            _forms.AddRange(Directory.GetFiles(SamplesFolder, "*.bpt").Select(p => FormEntry.Read(p)));
        _forms.AddRange(external);
        CollectionViewSource.GetDefaultView(_forms).Refresh();

        NoFormsText.Visibility = _forms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (selected is not null) SelectForm(selected, quiet: true);
    }

    private void SelectForm(string path, bool quiet = false)
    {
        var entry = _forms.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;
        _restoringForm = quiet;
        try { FormsList.SelectedItem = entry; }
        finally { _restoringForm = false; }
        FormsList.ScrollIntoView(entry);
    }

    private void FormsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringForm || SelectedForm is not { } entry) return;

        if (!TryOpenForm(entry.Path, out var error))
        {
            BpMessage.Show(this, $"Could not open {System.IO.Path.GetFileName(entry.Path)}: {error}",
                BpMessageKind.Error, title: "Open Form");
            // Back to the form that is open, without opening it again.
            var previous = e.RemovedItems.OfType<FormEntry>().FirstOrDefault();
            _restoringForm = true;
            try { FormsList.SelectedItem = previous; }
            finally { _restoringForm = false; }
            return;
        }

        // A new form reads the selected document again, so two forms can be compared at once.
        SetStatus($"Opened {entry.Name}.");
        ScheduleRetrieve();
    }

    private void OpenForm_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Form",
            Filter = "BluePrint report (*.bpt)|*.bpt",
            InitialDirectory = Directory.Exists(SamplesFolder) ? SamplesFolder : AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog(this) != true) return;

        var path = System.IO.Path.GetFullPath(dialog.FileName);
        if (!_forms.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            _forms.Add(FormEntry.Read(path, external: true));
            CollectionViewSource.GetDefaultView(_forms).Refresh();
            NoFormsText.Visibility = Visibility.Collapsed;
        }
        SelectForm(path);
    }
}
