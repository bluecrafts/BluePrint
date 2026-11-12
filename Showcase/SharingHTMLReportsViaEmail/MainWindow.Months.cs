// The Month list (bottom left): the months with orders, newest first, grouped by
// year. Choosing one reads it into the selected report.

using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SharingHtmlReports.Reports;

namespace SharingHtmlReports;

public partial class MainWindow
{
    private IReadOnlyList<MonthEntry> _months = [];

    public MonthEntry? SelectedMonth => MonthsList.SelectedItem as MonthEntry;

    private bool DatabaseExists => File.Exists(MonthCatalog.DatabasePath);

    private void InitializeMonths() => MonthCountText.Text = "Reading the database...";

    private async Task LoadMonthsAsync()
    {
        if (!DatabaseExists)
        {
            ShowNoDatabase("northwind.db was not found next to the application.");
            return;
        }
        try
        {
            ShowMonths(await Task.Run(() => MonthCatalog.Read()));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowNoDatabase($"northwind.db cannot be read: {ex.Message}");
        }
    }

    private void ShowNoDatabase(string text)
    {
        NoDatabaseText.Text = text;
        NoDatabasePanel.Visibility = Visibility.Visible;
        MonthCountText.Text = "";
        SetStatus(text, BluePrint.UI.Controls.BpMessageKind.Error);
        ShowPreviewMessage(text, BluePrint.UI.Controls.BpMessageKind.Error);
    }

    private void ShowMonths(IReadOnlyList<MonthEntry> months)
    {
        // Read before the list changes: filling it selects an item, and a selection is saved as the last month.
        var last = _config.LastMonth;
        _months = months;
        var view = new ListCollectionView(months.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MonthEntry.YearText)));
        MonthsList.ItemsSource = view;
        MonthCountText.Text = months.Count == 1 ? "1 month" : $"{months.Count.ToString(CultureInfo.InvariantCulture)} months";
        SelectMonth(last);
    }

    /// <summary>Selects the month "yyyy-MM", or the newest month that is not partial.</summary>
    public void SelectMonth(string? key)
    {
        var month = _months.FirstOrDefault(m => m.Key == key) ?? MonthCatalog.DefaultMonth(_months);
        MonthsList.SelectedItem = month;
        if (month is not null) MonthsList.ScrollIntoView(month);
    }

    private void MonthsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedMonth is { } month && _config.LastMonth != month.Key)
        {
            _config.LastMonth = month.Key;
            SaveConfig();
        }
        ScheduleRetrieve();
    }
}
