// The Forms list: the forms of the Samples folder grouped by where their
// translations are kept, plus any form opened with File > Open Form. Choosing a
// form opens it with its translation layers and reads the selected document into it.

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BluePrint.UI.Controls;
using LocalizationShowcase.Forms;
using Microsoft.Win32;

namespace LocalizationShowcase;

public partial class MainWindow
{
    private const string DefaultForm = "Invoice-Embedded";

    private readonly List<FormEntry> _forms = [];

    // Restoring the selection after a form failed to open must not open anything.
    private bool _restoringForm;

    // Asked by the commands while InitializeComponent still builds the window: the list may not be there yet.
    private FormEntry? SelectedForm => FormsList?.SelectedItem as FormEntry;

    private void InitializeForms()
    {
        var view = CollectionViewSource.GetDefaultView(_forms);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FormEntry.Group)));
        view.SortDescriptions.Add(new SortDescription(nameof(FormEntry.GroupOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(FormEntry.Name), ListSortDirection.Ascending));
        FormsList.ItemsSource = view;
        // The forms themselves are read when the window is on screen (MainWindow.xaml.cs).
    }

    // Reads the Samples folder again. Forms opened with Open Form stay; the selection stays on the same file.
    private void RefreshForms() => ShowForms(FormCatalog.Read(SamplesFolder));

    private void ShowForms(List<FormEntry> samples)
    {
        var selected = SelectedForm?.Path;
        var external = _forms.Where(f => f.IsExternal).ToList();

        _forms.Clear();
        _forms.AddRange(samples);
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
        ShowFormDescription();
        if (_restoringForm || SelectedForm is not { } entry) return;

        if (!TryOpenForm(entry))
        {
            // Back to the form that is open, without opening it again.
            var previous = e.RemovedItems.OfType<FormEntry>().FirstOrDefault();
            _restoringForm = true;
            try { FormsList.SelectedItem = previous; }
            finally { _restoringForm = false; }
            ShowFormDescription();
            return;
        }

        // A new form reads the selected document again, so two forms can be compared on one order.
        SetStatus($"Opened {entry.Name}.");
        RetrieveNow();
    }

    // Reload Form (F5): the same form read again from disk, with its translation files,
    // after they were changed in an editor. The document is read again too.
    private void ReloadForm_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (SelectedForm is not { } entry) return;
        var fresh = FormCatalog.Entry(entry.Path, entry.IsExternal);
        int index = _forms.IndexOf(entry);
        if (index >= 0) _forms[index] = fresh;
        CollectionViewSource.GetDefaultView(_forms).Refresh();
        SelectForm(fresh.Path, quiet: true);
        if (TryOpenForm(fresh))
        {
            SetStatus($"Reloaded {fresh.Name}.");
            RetrieveNow();
        }
    }

    private void HasForm_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = SelectedForm is not null;

    private void ShowFormDescription()
    {
        var text = SelectedForm?.ManifestError is { } problem
            ? problem + "\n\n" + SelectedForm.Description
            : SelectedForm?.Description;
        FormDescriptionText.Text = text ?? "";
        FormDescriptionPanel.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
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

        var path = Path.GetFullPath(dialog.FileName);
        if (!_forms.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            _forms.Add(FormCatalog.Entry(path, external: true));
            CollectionViewSource.GetDefaultView(_forms).Refresh();
            NoFormsText.Visibility = Visibility.Collapsed;
        }
        SelectForm(path);
    }

    /// <summary>
    /// Opens <paramref name="entry"/> for the next retrieve. A layer that cannot be loaded is
    /// reported once and left out; a report that cannot be read is an error and keeps the form that is open.
    /// </summary>
    private bool TryOpenForm(FormEntry entry)
    {
        OpenedForm opened;
        try
        {
            opened = FormLoader.Open(entry);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            BpMessage.Show(this, $"Could not open {Path.GetFileName(entry.Path)}: {ex.Message}",
                BpMessageKind.Error, title: "Open Form");
            return false;
        }

        _openForm = opened;
        ShowFormLanguages();
        ShowSources();
        UpdateTitle();

        if (entry.ManifestError is { } problem)
            BpMessage.Show(this, problem, BpMessageKind.Warning, title: entry.Name);
        foreach (var broken in opened.BrokenLayers)
            BpMessage.Show(this, $"A translation layer could not be loaded: {broken.Layer.Path}. {broken.Error}",
                BpMessageKind.Warning, title: entry.Name);
        return true;
    }

    /// <summary>The form chosen last, or Invoice-Embedded when there is none (or it is gone).</summary>
    private string StartupForm()
        => FromStoredPath(_config.LastForm) is { } last && File.Exists(last) ? last
           : Path.Combine(SamplesFolder, DefaultForm + ".bpt");
}
