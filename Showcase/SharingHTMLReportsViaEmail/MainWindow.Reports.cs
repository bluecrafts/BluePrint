// The Reports list (top left): every .bpt file in the Samples folder. Choosing one
// reads the selected month into it.

using System.Windows;
using System.Windows.Controls;
using SharingHtmlReports.Reports;

namespace SharingHtmlReports;

public partial class MainWindow
{
    private IReadOnlyList<ReportEntry> _reports = [];

    public ReportEntry? SelectedReport => ReportsList.SelectedItem as ReportEntry;

    private void ShowReports(IReadOnlyList<ReportEntry> reports)
    {
        _reports = reports;
        ReportsList.ItemsSource = reports;
        NoReportsText.Visibility = reports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Selects the report with this file name, or the first one that can be retrieved.</summary>
    public void SelectReport(string? fileName)
    {
        ReportsList.SelectedItem =
            _reports.FirstOrDefault(r => string.Equals(r.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            ?? _reports.FirstOrDefault(r => r.CanRetrieve)
            ?? _reports.FirstOrDefault();
    }

    private void ReportsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var report = SelectedReport;
        ReportDescriptionText.Text = report is null ? "" : report.Problem ?? report.Description;
        ReportDescriptionPanel.Visibility = string.IsNullOrEmpty(ReportDescriptionText.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        if (report is not null && _config.LastReport != report.FileName)
        {
            _config.LastReport = report.FileName;
            SaveConfig();
        }
        ScheduleRetrieve();
    }
}
