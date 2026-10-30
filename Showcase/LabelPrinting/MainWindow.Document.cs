// New, opening and saving the label. One file is open at a time; starting a new
// one, opening another, or closing the window first asks what to do with unsaved changes.

using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Input;
using BluePrint.Core.Models;
using BluePrint.UI.Controls;
using Microsoft.Win32;

namespace LabelPrinting;

public partial class MainWindow
{
    private const string ReportFilter = "BluePrint report (*.bpt)|*.bpt";

    private void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges()) return;

        // No file yet: the first Save asks for a name.
        Designer.LoadReportObject(NewLabelDefinition());
        SetStatus("New label. Retrieve reads the six columns of Label2Sticker.csv.");
        // An empty label has nothing to preview: start where it is designed.
        DesignTab.IsSelected = true;
        AfterLabelReplaced();
    }

    /// <summary>
    /// An empty label on the same layout and data columns as Label2Sticker.bpt, so
    /// Label2Sticker.csv (or any file with those six columns in that order) fills it.
    /// </summary>
    private static ReportDefinition NewLabelDefinition() => new()
    {
        Metadata = new ReportMetadata { Name = "New Label" },
        Report = new ReportSettings
        {
            Type = ReportType.Label,
            LayoutUnit = MeasureUnit.Cm,
            // A roll prints one row of labels per page, so the paper size is not
            // used; the margins are, and a roll has none.
            Page = new PageSettings
            {
                PaperSize = PaperSize.A4,
                Orientation = PageOrientation.Portrait,
                Margins = new PageMargins { Top = 0, Bottom = 0, Left = 0, Right = 0 },
            },
            LabelLayout = new LabelLayoutSettings
            {
                LabelWidth = 5,
                LabelHeight = 5,
                LabelsPerRow = 2,
                RowGap = 0,
                ColumnGap = 0,
                PageMode = LabelPageMode.RowPerPage,
            },
        },
        DataSource = new DataSourceDefinition
        {
            Name = "DataSource1",
            DataSourceType = DataSourceType.External,
            Columns =
            [
                new DataSourceColumn { Name = "ProductName", DbType = DbType.String, Length = 60 },
                new DataSourceColumn { Name = "ProductCode", DbType = DbType.String, Length = 20 },
                new DataSourceColumn { Name = "Price", DbType = DbType.Decimal, Decimals = 2 },
                new DataSourceColumn { Name = "LotNo", DbType = DbType.String, Length = 20 },
                new DataSourceColumn { Name = "ManufactureDate", DbType = DbType.Date },
                new DataSourceColumn { Name = "ExpireDate", DbType = DbType.Date },
            ],
        },
        // Dates and prices on the label do not follow the culture of the machine.
        Culture = "en-US",
        // A label report has a detail band only: one label.
        Sections = [new SectionDefinition { Type = BandType.Detail, Height = 5 }],
    };

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Label",
            Filter = ReportFilter,
            InitialDirectory = StartFolder(),
        };
        if (dialog.ShowDialog(this) == true) OpenPath(dialog.FileName);
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => TrySave(saveAs: false);

    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => TrySave(saveAs: true);

    /// <summary>Replaces the open label with the file at <paramref name="path"/>.</summary>
    /// <returns>false when the user kept the current label or the file could not be read.</returns>
    private bool OpenPath(string path)
    {
        if (!ConfirmDiscardChanges()) return false;

        try
        {
            Designer.LoadReport(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Anything from a missing file to JSON that is not a report: the label
            // on screen stays as it was, so nothing is lost by reporting and going on.
            BpMessage.Show(this, $"Could not open {Path.GetFileName(path)}.\n\n{ex.Message}",
                BpMessageKind.Error, title: "Open Label");
            SetStatus($"Could not open {Path.GetFileName(path)}.", BpMessageKind.Error);
            return false;
        }

        SetStatus($"Opened {Path.GetFileName(path)}.");
        RememberOpenedReport(path);
        AfterLabelReplaced();
        return true;
    }

    // A new label or another file is in the designer: the rows and the row image
    // belonged to the one before.
    private void AfterLabelReplaced()
    {
        UpdateViewState();
        // Replacing the label does not always change the selection (none before,
        // none after): show the new label's properties here.
        Properties.Reload();
        ResetRows();
        ScheduleRowImage();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Saves the label, asking for a file name when it has none or when <paramref name="saveAs"/>.</summary>
    /// <returns>true when the label is on disk afterwards; false when the user cancelled or saving failed.</returns>
    private bool TrySave(bool saveAs)
    {
        if (Designer.Report is null) return false;

        try
        {
            // Both show the designer's own Save As dialog when they need a name.
            // Neither says whether the user cancelled it; IsModified does.
            if (saveAs) Designer.SaveAs(); else Designer.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BpMessage.Show(this, $"Could not save the label.\n\n{ex.Message}",
                BpMessageKind.Error, title: "Save Label");
            SetStatus("Could not save the label.", BpMessageKind.Error);
            return false;
        }

        if (Designer.IsModified) return false;   // the Save As dialog was cancelled

        SetStatus($"Saved {Path.GetFileName(Designer.CurrentFilePath)}.", BpMessageKind.Success);
        if (Designer.CurrentFilePath is { } path) RememberOpenedReport(path);   // Save As may have moved it
        return true;
    }

    /// <summary>
    /// Asks what to do with unsaved changes before the label is replaced or the
    /// window closes.
    /// </summary>
    /// <returns>true to go ahead (saved, or the user chose not to); false to stay.</returns>
    private bool ConfirmDiscardChanges()
    {
        if (Designer.Report is null || !Designer.IsModified) return true;

        var name = Designer.CurrentFilePath is { } path ? Path.GetFileName(path) : Designer.Title;
        return BpMessage.Confirm(this, $"Save changes to \"{name}\"?", title: AppName) switch
        {
            BpConfirmResult.Yes => TrySave(saveAs: false),
            BpConfirmResult.No => true,
            _ => false,
        };
    }

    // The folder of the open label, or the application's own folder when none is open.
    private string StartFolder()
        => Path.GetDirectoryName(Designer.CurrentFilePath) is { Length: > 0 } folder
            ? folder
            : AppContext.BaseDirectory;
}
