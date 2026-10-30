// The View menu and what it controls: the Design and Preview tabs, the zoom of
// each, the panel on the right of each tab (Properties, Print), and the theme.

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BluePrint.Designer.Controls;
using BluePrint.UI;

namespace LabelPrinting;

public partial class MainWindow
{
    private const string DesignTabName = "Design";
    private const string PreviewTabName = "Preview";
    private const string OldPreviewTabName = "Monochrome";   // LabelPrinting.cfg before the tab was renamed

    // Zoom of the design surface in percent. A label is small: 200% is the
    // default, about the size of the Preview tab at 1x on a 96 dpi screen.
    private static readonly int[] DesignZooms = [50, 100, 150, 200, 300, 400];
    private const double MinDesignZoom = 0.25;
    private const double MaxDesignZoom = 8;

    private void InitializeView()
    {
        InitializeTheme();

        DesignZoomBox.ItemsSource = DesignZooms.Select(PercentText).ToList();
        Designer.Zoom = Math.Clamp(_config.App.DesignZoom, MinDesignZoom, MaxDesignZoom);
        ShowDesignZoom();
        // The designer can change its zoom itself (its zoom dialog): follow it.
        DependencyPropertyDescriptor.FromProperty(BluePrintDesignerControl.ZoomProperty, typeof(BluePrintDesignerControl))
            .AddValueChanged(Designer, (_, _) => ShowDesignZoom());

        // The properties of what is selected on the design surface; the header says what that is.
        Properties.SubjectChanged += subject => PropertiesSubject.Text = subject;
        Properties.Attach(Designer);

        Monochrome.ZoomText = _config.App.RowImageZoom;
        ShowRightPanel(_config.App.PrintPanelVisible, _config.App.PrintPanelWidth);

        if (_config.App.ActiveTab is PreviewTabName or OldPreviewTabName) PreviewTab.IsSelected = true;
        UpdateViewState();

        Designer.TitleChanged += UpdateViewState;
    }

    private void RememberView()
    {
        _config.App.PrintPanelVisible = RightPanelMenuItem.IsChecked;
        if (_config.App.PrintPanelVisible) _config.App.PrintPanelWidth = ActivePaneColumn.ActualWidth;
        _config.App.RowImageZoom = Monochrome.ZoomText;
        _config.App.DesignZoom = Designer.Zoom;
        _config.App.ActiveTab = PreviewTab.IsSelected ? PreviewTabName : DesignTabName;
    }

    // ---- theme --------------------------------------------------------------------

    // Light, Dark, or System (the Windows app mode). Nothing switches the BluePrint
    // theme on its own: it stays light until the application calls SetMode.
    private void InitializeTheme()
    {
        BpTheme.SetMode(Enum.TryParse<BpThemeMode>(_config.App.Theme, ignoreCase: true, out var mode)
            ? mode : BpThemeMode.System);

        // The ready-made Theme menu of BluePrint.UI, below Right Panel.
        var view = (MenuItem)ThemeSeparator.Parent;
        view.Items.Insert(view.Items.IndexOf(ThemeSeparator) + 1, BpThemeMenu.Create("_Theme"));

        // Remember what the user asked for (System stays System), not the light or
        // dark it turned into.
        BpTheme.ModeChanged += _ =>
        {
            _config.App.Theme = BpTheme.Mode.ToString();
            SaveConfig();
        };
    }

    // ---- tabs --------------------------------------------------------------------

    private void ShowDesign_Executed(object sender, ExecutedRoutedEventArgs e) => DesignTab.IsSelected = true;

    private void ShowPreview_Executed(object sender, ExecutedRoutedEventArgs e) => PreviewTab.IsSelected = true;

    /// <summary>
    /// After Retrieve: from the Design tab, go to the Preview tab to see the labels
    /// with their data. Later, once the designer has put the new rows in place.
    /// </summary>
    private void ShowPreviewAfterRetrieve()
    {
        if (DesignTab.IsSelected)
            Dispatcher.BeginInvoke(() => PreviewTab.IsSelected = true, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ViewTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection changes of the combo boxes inside the tabs bubble up to here too.
        if (!ReferenceEquals(e.OriginalSource, ViewTabs)) return;

        // Both tabs have a panel on the right: keep it as wide as the user left it.
        if (RightPanelMenuItem.IsChecked)
        {
            var width = (DesignTab.IsSelected ? PrintColumn : PropertiesColumn).ActualWidth;
            if (width > 0) ActivePaneColumn.Width = new GridLength(width);
        }

        UpdateViewState();
    }

    // What depends on the tab and on whether a label is open.
    private void UpdateViewState()
    {
        // The window is still being built when the first tab is selected.
        if (DesignZoomPanel is null || ElementsToolBar is null) return;

        var hasLabel = Designer.Report is not null;
        NoLabelText.Visibility = hasLabel ? Visibility.Collapsed : Visibility.Visible;
        DesignZoomPanel.Visibility = DesignTab.IsSelected && hasLabel ? Visibility.Visible : Visibility.Collapsed;
        UpdateElementTools();
    }

    // ---- zoom --------------------------------------------------------------------

    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) => StepZoom(+1);

    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) => StepZoom(-1);

    private void StepZoom(int direction)
    {
        if (PreviewTab.IsSelected)
        {
            Monochrome.StepZoom(direction);
            return;
        }

        // The next step of the list from the zoom there is now, which may be one typed in.
        var percent = Designer.Zoom * 100;
        var next = direction > 0
            ? DesignZooms.Where(z => z > percent + 0.5).DefaultIfEmpty(DesignZooms[^1]).First()
            : DesignZooms.Where(z => z < percent - 0.5).DefaultIfEmpty(DesignZooms[0]).Last();
        Designer.Zoom = next / 100.0;
    }

    private static string PercentText(int percent) => percent.ToString(CultureInfo.InvariantCulture) + "%";

    private void ShowDesignZoom()
    {
        var text = PercentText((int)Math.Round(Designer.Zoom * 100));
        if (DesignZoomBox.Text != text) DesignZoomBox.Text = text;
    }

    private void DesignZoomBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DesignZoomBox.SelectedItem is string text) ApplyDesignZoom(text);
    }

    private void DesignZoomBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyDesignZoom(DesignZoomBox.Text);
        e.Handled = true;
    }

    private void DesignZoomBox_Commit(object sender, RoutedEventArgs e) => ApplyDesignZoom(DesignZoomBox.Text);

    // "150%" or "150"; anything else puts the current zoom back.
    private void ApplyDesignZoom(string? text)
    {
        var trimmed = (text ?? "").Trim().TrimEnd('%').Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            && percent >= MinDesignZoom * 100 && percent <= MaxDesignZoom * 100)
            Designer.Zoom = percent / 100;
        ShowDesignZoom();
    }

    // ---- the panel on the right of each tab ------------------------------------------

    private ColumnDefinition ActivePaneColumn => PreviewTab.IsSelected ? PrintColumn : PropertiesColumn;

    // Properties in the Design tab, Print in the Preview tab: shown or hidden together.
    private void ShowRightPanel(bool visible, double width)
    {
        RightPanelMenuItem.IsChecked = visible;
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PropertiesPane.Visibility = PropertiesSplitter.Visibility = visibility;
        PrintPane.Visibility = PrintSplitter.Visibility = visibility;
        foreach (var column in new[] { PropertiesColumn, PrintColumn })
        {
            column.MinWidth = visible ? 260 : 0;
            column.Width = visible ? new GridLength(Math.Max(260, width)) : new GridLength(0);
        }
    }

    private void RightPanelMenuItem_Click(object sender, RoutedEventArgs e)
        => ShowRightPanel(RightPanelMenuItem.IsChecked, _config.App.PrintPanelWidth);
}
