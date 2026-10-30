// Filling the label with rows. This application reads label data from a file
// (CSV, Excel, text or JSON) - the report's data source is "external", so the
// report carries the column list but no query. The designer never opens a file
// or a connection itself: on Retrieve it hands over an empty BluePrintDocument
// for the label being designed, and the code below puts the rows in it.

using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Input;
using BluePrint.Core.Models;
using BluePrint.Render;
using BluePrint.Render.Data;
using BluePrint.Render.WPF.Dialogs;
using BluePrint.UI.Controls;

namespace LabelPrinting;

public partial class MainWindow
{
    private void ConfigureRetrieve()
    {
        Designer.RetrieveReportFactory = (document, _) => Task.FromResult(FillFromFile(document));
    }

    private void Retrieve_Executed(object sender, ExecutedRoutedEventArgs e) => Designer.Retrieve();

    /// <summary>
    /// Asks which file to read, reads it into a table shaped by the report's
    /// columns, and gives the table to <paramref name="document"/>.
    /// </summary>
    /// <returns>true when the rows are in; false keeps the rows of the previous retrieve.</returns>
    private bool FillFromFile(BluePrintDocument document)
    {
        var dataSource = document.Definition.DataSource;
        if (dataSource is null || dataSource.DataSourceType != DataSourceType.External)
        {
            BpMessage.Show(this,
                "This sample reads label data from Excel or CSV files only. " +
                "The report's data source is a database query.",
                BpMessageKind.Information, title: "Retrieve");
            return false;
        }

        // The settings used last time for this report (file, sheet, delimiter...),
        // or the data file that sits next to it. Render keeps them per report file.
        // A new label has no file yet; it has the sample's columns, so it starts
        // on the sample's data.
        var reportPath = Designer.CurrentFilePath;
        var dialog = new ExternalImportDialog(dataSource.Columns ?? [],
                                              ExternalImportMemory.Get(reportPath) ?? DataFileNextTo(reportPath ?? SampleLabelPath))
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.ResultTable is not DataTable table) return false;   // Cancel

        if (dialog.ResultOptions is { } options)
            ExternalImportMemory.Set(reportPath, options);

        document.SetData(table);

        var source = dialog.ResultOptions is { FilePath.Length: > 0 } used ? Path.GetFileName(used.FilePath) : "the file";
        SetStatus($"Retrieved {table.Rows.Count} rows from {source}.");

        // New rows: new images. The designer puts this document in place as soon
        // as this returns, before the timer fires.
        ResetRows();
        ScheduleRowImage();
        ShowPreviewAfterRetrieve();
        return true;
    }

    // Label2Sticker.bpt reads Label2Sticker.csv: a data file with the report's
    // name, in the report's folder - CSV first, then Excel.
    private static ExternalImportOptions? DataFileNextTo(string? reportPath)
    {
        if (reportPath is null) return null;

        var stem = Path.ChangeExtension(reportPath, null);
        if (File.Exists(stem + ".csv"))
            return new ExternalImportOptions { Format = ExternalImportFormat.Csv, FilePath = stem + ".csv", Delimiter = ",", HasHeader = true };
        if (File.Exists(stem + ".xlsx"))
            return new ExternalImportOptions { Format = ExternalImportFormat.Excel, FilePath = stem + ".xlsx", HasHeader = true };
        return null;
    }
}
