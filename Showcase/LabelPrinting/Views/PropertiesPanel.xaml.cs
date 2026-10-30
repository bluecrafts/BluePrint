using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BluePrint.Core.Models;
using BluePrint.Designer.Controls;
using BluePrint.Expression.Functions;
using BluePrint.Render.Engine;
using BluePrint.UI.Controls;
using Microsoft.Win32;

namespace LabelPrinting.Views;

/// <summary>
/// The Properties panel: what a label needs, for the label itself (nothing
/// selected) or for the elements selected on the design surface.
/// <para>
/// It never changes the report directly. Every change goes through
/// <see cref="BluePrintDesignerControl.ApplyElementChange"/> or
/// <see cref="BluePrintDesignerControl.ApplyReportChange"/>, which record an undo
/// step, keep the unit conversions right, redraw the surface and mark the label
/// as changed.
/// </para>
/// <para>
/// A typed value is applied on Enter or when the field loses the focus. One that
/// cannot be used is put back, and the reason is shown at the top of the panel.
/// </para>
/// </summary>
public partial class PropertiesPanel : UserControl
{
    private BluePrintDesignerControl? _designer;
    private IReadOnlyList<ElementDefinition> _elements = [];
    private bool _loading;
    private string? _pendingError;

    // Fields typed into since they were filled, and how to apply each one.
    private readonly HashSet<Control> _dirty = [];
    private readonly Dictionary<Control, Func<string, string?>> _commits = [];

    private sealed record ColorChoice(string Name, string Value, Brush Brush);

    private static readonly ColorChoice[] TextColors = [ColorOf("Black", "#000000"), ColorOf("White", "#FFFFFF"), ColorOf("Gray", "#808080")];
    private static readonly ColorChoice[] BackColors = [ColorOf("None", "Transparent"), .. TextColors];

    private static readonly string[] FontSizes = ["6", "7", "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72"];
    private const string NoFormat = "(none)";
    private static readonly string[] Formats = [NoFormat, "#,##0", "#,##0.00", "0", "0.00", "dd/MM/yyyy", "yyyy-MM-dd", "HH:mm"];

    private static readonly (string Name, BarcodeSymbology Value)[] BarcodeTypes =
    [
        ("Code 128", BarcodeSymbology.Code128), ("GS1-128", BarcodeSymbology.GS1_128), ("Code 39", BarcodeSymbology.Code39),
        ("EAN-13", BarcodeSymbology.EAN13), ("EAN-8", BarcodeSymbology.EAN8), ("UPC-A", BarcodeSymbology.UPCA),
        ("UPC-E", BarcodeSymbology.UPCE), ("ITF", BarcodeSymbology.ITF), ("Codabar", BarcodeSymbology.Codabar),
        ("QR Code", BarcodeSymbology.QRCode), ("Data Matrix", BarcodeSymbology.DataMatrix),
        ("GS1 DataMatrix", BarcodeSymbology.GS1DataMatrix), ("PDF417", BarcodeSymbology.PDF417), ("Aztec", BarcodeSymbology.Aztec),
    ];

    private static readonly HashSet<BarcodeSymbology> TwoDimensional =
    [
        BarcodeSymbology.QRCode, BarcodeSymbology.DataMatrix, BarcodeSymbology.GS1DataMatrix,
        BarcodeSymbology.PDF417, BarcodeSymbology.Aztec,
    ];

    /// <summary>What the panel is showing: "Label", "Field", "3 elements"... Empty with no label open.</summary>
    public string Subject { get; private set; } = "";

    public event Action<string>? SubjectChanged;

    public PropertiesPanel()
    {
        InitializeComponent();

        FillChoices(SizeModeBox, [("Fit", ImageSizeMode.Zoom), ("Stretch", ImageSizeMode.Stretch), ("Actual size", ImageSizeMode.Normal)]);
        FillChoices(BarcodeTypeBox, BarcodeTypes.Select(t => (t.Name, (object)t.Value)));
        FillChoices(ShapeBox, [("Rectangle", GeometricShape.Rectangle), ("Rounded rectangle", GeometricShape.RoundRectangle), ("Ellipse", GeometricShape.Ellipse)]);
        // The width of a black line on all four sides; 0 = no border.
        FillChoices(BorderBox, [("None", 0.0), ("Thin", 1.0), ("Medium", 2.0), ("Thick", 3.0)]);

        ForeColorBox.ItemsSource = TextColors;
        BackColorBox.ItemsSource = BackColors;
        LineColorBox.ItemsSource = TextColors;
        FillBox.ItemsSource = BackColors;
        FontSizeBox.ItemsSource = FontSizes;
        FormatBox.ItemsSource = Formats;

        WireLabelFields();
        WireElementFields();
    }

    /// <summary>Follows this designer: its selection, and every change to the label.</summary>
    public void Attach(BluePrintDesignerControl designer)
    {
        _designer = designer;
        designer.SelectionChanged += () =>
        {
            // A value typed for the elements selected until now belongs to them.
            CommitPending();
            Reload();
        };
        designer.DesignChanged += Reload;
        Reload();
    }

    // ---- showing what is selected -------------------------------------------------

    /// <summary>Fills the panel from the label and the selection as they are now.</summary>
    public void Reload()
    {
        if (_designer is null) return;
        _loading = true;
        try
        {
            ErrorLines.SetLines(BpMessageKind.Error, _pendingError is null ? [] : [_pendingError]);
            _pendingError = null;
            LabelSection.Visibility = ElementSection.Visibility = MessageText.Visibility = Visibility.Collapsed;

            if (_designer.Report is not { } report)
            {
                _elements = [];
                SetSubject("");
                return;
            }

            _elements = _designer.SelectedElements;
            if (_elements.Count == 0)
            {
                LoadLabel(report);
                SetSubject("Label");
                return;
            }

            var groups = PropertyMatrix.For(_elements.Select(e => e.Type).ToList());
            SetSubject(_elements.Count == 1 ? PropertyMatrix.DisplayName(_elements[0].Type) : $"{_elements.Count} elements");
            if (groups.Count == 0)
            {
                ShowMessage(_elements.Count == 1
                    ? "This element has no properties here. Move and size it on the label."
                    : $"{_elements.Count} elements are selected. They have no properties in common here.");
                return;
            }

            ElementSection.Visibility = Visibility.Visible;
            foreach (var group in Enum.GetValues<PropertyGroup>())
                GroupPanel(group).Visibility = groups.Contains(group) ? Visibility.Visible : Visibility.Collapsed;
            LoadElements(report, groups);
        }
        finally
        {
            _loading = false;
            _dirty.Clear();
        }
    }

    private void SetSubject(string subject)
    {
        Subject = subject;
        SubjectChanged?.Invoke(subject);
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.Visibility = Visibility.Visible;
    }

    private FrameworkElement GroupPanel(PropertyGroup group) => (FrameworkElement)FindName("Group" + group);

    private void LoadLabel(ReportDefinition report)
    {
        if (report.Report.Type != ReportType.Label || report.Report.LabelLayout is not { } layout)
        {
            ShowMessage("Only label reports can be edited here.");
            return;
        }

        LabelSection.Visibility = Visibility.Visible;
        var unit = UnitName(report.Report.LayoutUnit);
        LabelUnit1.Text = LabelUnit2.Text = LabelUnit3.Text = unit;
        LabelUnit4.Text = LabelUnit5.Text = LabelUnit6.Text = LabelUnit7.Text = unit;

        LabelWidthBox.Text = Number(layout.LabelWidth);
        LabelHeightBox.Text = Number(layout.LabelHeight);
        LabelsPerRowBox.Text = layout.LabelsPerRow.ToString(CultureInfo.InvariantCulture);
        LabelGapBox.Text = Number(layout.ColumnGap);

        var margins = report.Report.Page.Margins ?? new PageMargins();
        MarginLeftBox.Text = Number(margins.Left);
        MarginRightBox.Text = Number(margins.Right);
        MarginTopBox.Text = Number(margins.Top);
        MarginBottomBox.Text = Number(margins.Bottom);

        var across = Math.Max(1, layout.LabelsPerRow);
        var gaps = across - 1;
        var rowWidth = across * layout.LabelWidth + gaps * layout.ColumnGap;
        RowWidthText.Text = $"Row width {Number(rowWidth)} {unit} ({across} label{Plural(across)} and {gaps} gap{Plural(gaps)}).";

        var roll = layout.PageMode == LabelPageMode.RowPerPage;

        // On a roll the page is one row of labels: the same sum the render engine
        // makes, and the size the designer's own Properties panel shows. The gap
        // between rows is not part of it; it is the gap on the roll.
        PageSizeText.Text = roll
            ? $"{Number(margins.Left + rowWidth + margins.Right)} x {Number(margins.Top + layout.LabelHeight + margins.Bottom)} {unit}  (from label size)"
            : "The paper size (sheet layout)";
        PageSizeText.ToolTip = roll
            ? "One row of labels: the margins, the labels and the column gaps across; the margins and the label height down."
            : null;

        SheetLines.SetLines(BpMessageKind.Warning, roll
            ? []
            : ["This label is laid out for sheets. Only roll labels (one row per page) can be printed."]);
        UseRollButton.Visibility = roll ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LoadElements(ReportDefinition report, IReadOnlyList<PropertyGroup> groups)
    {
        var first = _elements[0];

        if (groups.Contains(PropertyGroup.Text)) TextValueBox.Text = first.Text ?? "";

        if (groups.Contains(PropertyGroup.Column))
        {
            ColumnBox.ItemsSource = report.DataSource?.Columns?.Select(c => c.Name).ToList() ?? [];
            ColumnBox.Text = first.Source ?? "";
        }

        if (groups.Contains(PropertyGroup.Expression))
        {
            ExpressionBox.Text = first.Expression ?? "";
            ExpressionBox.ToolTip = string.IsNullOrEmpty(first.Expression) ? "The expression of the element" : first.Expression;
        }

        if (groups.Contains(PropertyGroup.Picture))
        {
            ImageFileBox.Text = first.ImagePath
                                ?? (string.IsNullOrEmpty(first.Expression) ? "" : "(from expression)");
            ImageFileBox.ToolTip = string.IsNullOrEmpty(ImageFileBox.Text) ? null : ImageFileBox.Text;
            SelectChoice(SizeModeBox, Common(e => (object)e.SizeMode));
        }

        if (groups.Contains(PropertyGroup.Barcode))
        {
            var symbology = Common(e => (object)(e.Barcode ?? new BarcodeSettings()).Symbology);
            SelectChoice(BarcodeTypeBox, symbology);
            ShowTextCheck.IsChecked = CommonBool(e => e.Barcode?.ShowText ?? true);
            ShowTextCheck.IsEnabled = !_elements.All(e => TwoDimensional.Contains((e.Barcode ?? new BarcodeSettings()).Symbology));
        }

        if (groups.Contains(PropertyGroup.Shape)) SelectChoice(ShapeBox, Common(e => (object)e.Geometric));

        if (groups.Contains(PropertyGroup.Font))
        {
            // The list of fonts is read once, the first time it is needed.
            FontBox.ItemsSource ??= Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
            FontBox.Text = CommonText(e => e.Style.FontFamily);
            FontSizeBox.Text = CommonText(e => Number(e.Style.FontSize));
            BoldToggle.IsChecked = CommonBool(e => e.Style.Bold);
            ItalicToggle.IsChecked = CommonBool(e => e.Style.Italic);
        }

        if (groups.Contains(PropertyGroup.Color))
        {
            ShowColor(ForeColorBox, CommonText(e => e.Style.ForeColor));
            ShowColor(BackColorBox, CommonText(e => e.Style.BackColor));
        }

        if (groups.Contains(PropertyGroup.Line))
        {
            LineWidthBox.Text = CommonText(e => Number(e.PenWidth));
            ShowColor(LineColorBox, CommonText(e => e.PenColor));
        }

        if (groups.Contains(PropertyGroup.Fill)) ShowColor(FillBox, CommonText(e => e.Style.BackColor));

        if (groups.Contains(PropertyGroup.Align))
        {
            AlignLeft.IsChecked = _elements.All(e => e.Style.HAlign == HAlign.Left);
            AlignCenter.IsChecked = _elements.All(e => e.Style.HAlign == HAlign.Center);
            AlignRight.IsChecked = _elements.All(e => e.Style.HAlign == HAlign.Right);
            AlignTop.IsChecked = _elements.All(e => e.Style.VAlign == VAlign.Top);
            AlignMiddle.IsChecked = _elements.All(e => e.Style.VAlign == VAlign.Middle);
            AlignBottom.IsChecked = _elements.All(e => e.Style.VAlign == VAlign.Bottom);
        }

        if (groups.Contains(PropertyGroup.Format))
            FormatBox.Text = CommonText(e => string.IsNullOrEmpty(e.Format) ? NoFormat : e.Format);

        if (groups.Contains(PropertyGroup.WrapText)) WrapCheck.IsChecked = CommonBool(e => e.WrapText);

        if (groups.Contains(PropertyGroup.Border)) SelectChoice(BorderBox, Common(e => BorderKind(e.Style.Border)), "Custom");

        if (groups.Contains(PropertyGroup.Position))
        {
            XBox.Text = Number(first.Bounds.X);
            YBox.Text = Number(first.Bounds.Y);
            WBox.Text = Number(first.Bounds.W);
            HBox.Text = Number(first.Bounds.H);
            PositionUnit.Text = UnitName(report.Report.LayoutUnit);
        }
    }

    // The value all the selected elements share; null (shown empty) when they differ.
    private object? Common(Func<ElementDefinition, object?> read)
    {
        var value = read(_elements[0]);
        return _elements.Skip(1).All(e => Equals(read(e), value)) ? value : null;
    }

    private string CommonText(Func<ElementDefinition, string?> read) => Common(read) as string ?? "";

    private bool? CommonBool(Func<ElementDefinition, bool> read) => (bool?)Common(e => read(e));

    // A border the list can show: 0 none, 1 2 3 a solid black line of that width
    // on all four sides. Anything else (a colour, some sides, dashes) is "Custom".
    private static object BorderKind(ElementBorder? border)
    {
        var sides = new[] { border?.ResolvedTop, border?.ResolvedRight, border?.ResolvedBottom, border?.ResolvedLeft };
        if (sides.All(s => s is null || s.Style == BorderLineStyle.None)) return 0.0;
        if (border is { Uniform: true, All: { Style: BorderLineStyle.Solid } all }
            && IsBlack(all.Color) && all.Width is 1 or 2 or 3)
            return all.Width;
        return "Custom";
    }

    private static bool IsBlack(string color)
        => TryParseColor(color, out var c) && c is { R: 0, G: 0, B: 0, A: 255 };

    // ---- applying a change ---------------------------------------------------------

    private void Edit(Action<ElementDefinition> change)
    {
        if (_designer is null || _elements.Count == 0) return;
        try
        {
            _designer.ApplyElementChange(_elements, change);
        }
        catch (ArgumentException)
        {
            // The selection was replaced meanwhile (an undo): nothing to change.
        }
        Reload();
    }

    private void EditLabel(Action<LabelLayoutSettings> change)
    {
        if (_designer?.Report is null) return;
        _designer.ApplyReportChange(r => change(r.Report.LabelLayout ??= new LabelLayoutSettings()));
        Reload();
    }

    private void CommitPending()
    {
        foreach (var control in _dirty.ToList()) Commit(control);
    }

    private void Commit(Control control)
    {
        if (_loading || !_dirty.Remove(control)) return;
        var text = control is TextBox box ? box.Text : ((ComboBox)control).Text;
        var error = _commits[control](text.Trim());
        if (error is null) return;
        _pendingError = error;
        Reload();
    }

    /// <summary>A field whose typed value is applied on Enter or when it loses the focus.</summary>
    private void Typed(Control control, string name, Func<string, string?> apply)
    {
        _commits[control] = text => apply(text) is { } error ? $"{name}: {error}" : null;

        if (control is TextBox box)
        {
            box.TextChanged += (_, _) => { if (!_loading) _dirty.Add(box); };
            box.LostKeyboardFocus += (_, _) => Commit(box);
            // A text of several lines takes Enter as a new line.
            if (!box.AcceptsReturn)
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(box); e.Handled = true; } };
            return;
        }

        var combo = (ComboBox)control;
        combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (!_loading) _dirty.Add(combo); }));
        combo.LostKeyboardFocus += (_, _) => { if (!combo.IsKeyboardFocusWithin) Commit(combo); };
        combo.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(combo); e.Handled = true; } };
        // Picked from the list: apply it once the combo box has put its text in place.
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is null) return;
            _dirty.Add(combo);
            Dispatcher.BeginInvoke(() => Commit(combo));
        };
    }

    private void WireLabelFields()
    {
        Typed(LabelWidthBox, "Width", t => Positive(t, v => EditLabel(l => l.LabelWidth = v)));
        Typed(LabelHeightBox, "Height", t => Positive(t, v => EditLabel(l => l.LabelHeight = v)));
        Typed(LabelsPerRowBox, "Labels per row", t =>
            int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 20
                ? Done(() => EditLabel(l => l.LabelsPerRow = n))
                : "Enter a number from 1 to 20.");
        Typed(LabelGapBox, "Column gap", t => TryNumber(t, out var v) && v >= 0
            ? Done(() => EditLabel(l => l.ColumnGap = v))
            : "Enter a number of 0 or more.");

        Typed(MarginLeftBox, "Left margin", t => EditMargin(t, (m, v) => m.Left = v));
        Typed(MarginRightBox, "Right margin", t => EditMargin(t, (m, v) => m.Right = v));
        Typed(MarginTopBox, "Top margin", t => EditMargin(t, (m, v) => m.Top = v));
        Typed(MarginBottomBox, "Bottom margin", t => EditMargin(t, (m, v) => m.Bottom = v));
    }

    // The designer keeps the margins in step with the unit of the label (BPU).
    private string? EditMargin(string text, Action<PageMargins, double> set)
    {
        if (!TryNumber(text, out var v) || v < 0) return "Enter a number of 0 or more.";
        if (_designer?.Report is null) return null;
        _designer.ApplyReportChange(r => set(r.Report.Page.Margins ??= new PageMargins(), v));
        Reload();
        return null;
    }

    private void WireElementFields()
    {
        Typed(TextValueBox, "Text", t => Done(() => Edit(e => e.Text = TextValueBox.Text)));
        Typed(ColumnBox, "Column", t => t.Length == 0 ? "Enter a column name." : Done(() => Edit(e => e.Source = t)));
        Typed(ExpressionBox, "Expression", t => Done(() => Edit(e => e.Expression = t.Length == 0 ? null : t)));
        Typed(FontBox, "Font", t => t.Length == 0 ? "Enter a font name." : Done(() => Edit(e => e.Style.FontFamily = t)));
        Typed(FontSizeBox, "Font size", t => TryNumber(t, out var v) && v is >= 1 and <= 400
            ? Done(() => Edit(e => e.Style.FontSize = v))
            : "Enter a number from 1 to 400.");
        Typed(ForeColorBox, "Color", t => ColorValue(t, TextColors, v => Edit(e => e.Style.ForeColor = v)));
        Typed(BackColorBox, "Back", t => ColorValue(t, BackColors, v => Edit(e => e.Style.BackColor = v)));
        Typed(LineWidthBox, "Line", t => TryNumber(t, out var v) && v is >= 0.5 and <= 20
            ? Done(() => Edit(e => e.PenWidth = v))
            : "Enter a number from 0.5 to 20.");
        Typed(LineColorBox, "Line color", t => ColorValue(t, TextColors, v => Edit(e => e.PenColor = v)));
        Typed(FillBox, "Fill", t => ColorValue(t, BackColors, v => Edit(e => e.Style.BackColor = v)));
        Typed(FormatBox, "Format", t => Done(() => Edit(e => e.Format = t.Length == 0 || t == NoFormat ? null : t)));
        Typed(XBox, "X", t => TryNumber(t, out var v) && v >= 0 ? Done(() => Edit(e => e.Bounds.X = v)) : "Enter a number of 0 or more.");
        Typed(YBox, "Y", t => TryNumber(t, out var v) && v >= 0 ? Done(() => Edit(e => e.Bounds.Y = v)) : "Enter a number of 0 or more.");
        Typed(WBox, "W", t => Positive(t, v => Edit(e => e.Bounds.W = v)));
        Typed(HBox, "H", t => Positive(t, v => Edit(e => e.Bounds.H = v)));

        Chosen(SizeModeBox, v => Edit(e => e.SizeMode = (ImageSizeMode)v));
        Chosen(BarcodeTypeBox, v => Edit(e => (e.Barcode ??= new BarcodeSettings()).Symbology = (BarcodeSymbology)v));
        Chosen(ShapeBox, v => Edit(e => e.Geometric = (GeometricShape)v));
        Chosen(BorderBox, v =>
        {
            var width = (double)v;
            Edit(e => e.Style.Border = width == 0 ? null : ElementBorder.CreateUniform(BorderLineStyle.Solid, "#000000", width));
        });

        ShowTextCheck.Click += (_, _) => Edit(e => (e.Barcode ??= new BarcodeSettings()).ShowText = ShowTextCheck.IsChecked == true);
        BoldToggle.Click += (_, _) => Edit(e => e.Style.Bold = BoldToggle.IsChecked == true);
        ItalicToggle.Click += (_, _) => Edit(e => e.Style.Italic = ItalicToggle.IsChecked == true);
        WrapCheck.Click += (_, _) => Edit(e => e.WrapText = WrapCheck.IsChecked == true);

        AlignLeft.Click += (_, _) => Edit(e => e.Style.HAlign = HAlign.Left);
        AlignCenter.Click += (_, _) => Edit(e => e.Style.HAlign = HAlign.Center);
        AlignRight.Click += (_, _) => Edit(e => e.Style.HAlign = HAlign.Right);
        AlignTop.Click += (_, _) => Edit(e => e.Style.VAlign = VAlign.Top);
        AlignMiddle.Click += (_, _) => Edit(e => e.Style.VAlign = VAlign.Middle);
        AlignBottom.Click += (_, _) => Edit(e => e.Style.VAlign = VAlign.Bottom);
    }

    private static string? Done(Action apply)
    {
        apply();
        return null;
    }

    private static string? Positive(string text, Action<double> apply)
        => TryNumber(text, out var v) && v > 0 ? Done(() => apply(v)) : "Enter a number greater than 0.";

    private static string? ColorValue(string text, ColorChoice[] choices, Action<string> apply)
    {
        var named = choices.FirstOrDefault(c => string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase));
        if (named is not null) return Done(() => apply(named.Value));
        if (Regex.IsMatch(text, "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")) return Done(() => apply(text.ToUpperInvariant()));
        return "Enter a color name or #RRGGBB.";
    }

    // ---- buttons -------------------------------------------------------------------

    private void UseRoll_Click(object sender, RoutedEventArgs e)
        => EditLabel(l => l.PageMode = LabelPageMode.RowPerPage);

    // The designer's own expression editor, with the columns of the data and the
    // other Compute elements of the label.
    private void ExpressionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_designer?.Report is not { } report || _elements.Count != 1) return;
        var element = _elements[0];

        var columns = report.DataSource?.Columns?.Select(c => c.Name).ToList() ?? [];
        var computes = new ComputeFieldTypeMap();
        foreach (var (name, type) in ExpressionDiagnostics.ComputeFieldTypes(report, ExternalFunctions.Default))
            if (!string.Equals(name, element.Name, StringComparison.OrdinalIgnoreCase)) computes[name] = type;

        var result = _designer.ExpressionDialogFactory(element.Expression ?? "", columns, computes,
                                                       report.DataSource, report.LookupDataSources);
        if (result is not null) Edit(el => el.Expression = result.Length == 0 ? null : result);
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null) return;
        var labelFolder = Path.GetDirectoryName(_designer.CurrentFilePath);
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Picture",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
            InitialDirectory = labelFolder ?? "",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        // A picture in the label's folder (or below it) is kept relative to it, so
        // the folder can be moved or copied as a whole.
        var path = dialog.FileName;
        if (labelFolder is not null)
        {
            var relative = Path.GetRelativePath(labelFolder, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative)) path = relative;
        }
        Edit(el =>
        {
            el.ImagePath = path;
            el.Expression = null;   // the file replaces a picture taken from the data
        });
    }

    // ---- lists of choices ------------------------------------------------------------

    // Items carry their value in Tag. The last item, disabled and hidden, shows a
    // value in the file that the list does not offer, until another one is chosen.
    private static void FillChoices(ComboBox box, IEnumerable<(string Name, object Value)> choices)
    {
        foreach (var (name, value) in choices) box.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        box.Items.Add(new ComboBoxItem { IsEnabled = false, Visibility = Visibility.Collapsed });
    }

    private static void SelectChoice(ComboBox box, object? value, string? otherName = null)
    {
        var items = box.Items.Cast<ComboBoxItem>().ToList();
        var other = items[^1];
        other.Visibility = Visibility.Collapsed;

        if (value is null) { box.SelectedIndex = -1; return; }   // the selected elements differ
        var match = items.FirstOrDefault(i => i != other && Equals(i.Tag, value));
        if (match is not null) { box.SelectedItem = match; return; }

        other.Content = otherName ?? value.ToString();
        other.Visibility = Visibility.Visible;
        box.SelectedItem = other;
    }

    private void Chosen(ComboBox box, Action<object> apply)
        => box.SelectionChanged += (_, _) =>
        {
            if (!_loading && box.SelectedItem is ComboBoxItem { IsEnabled: true, Tag: { } value }) apply(value);
        };

    private void ShowColor(ComboBox box, string value)
    {
        var choices = (ColorChoice[])box.ItemsSource;
        var match = TryParseColor(value, out var color)
            ? choices.FirstOrDefault(c => TryParseColor(c.Value, out var other) && other == color)
            : null;
        box.SelectedItem = match;
        box.Text = match?.Name ?? value;
    }

    private static ColorChoice ColorOf(string name, string value)
    {
        var brush = TryParseColor(value, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
        brush.Freeze();
        return new ColorChoice(name, value, brush);
    }

    private static bool TryParseColor(string? text, out System.Windows.Media.Color color)
    {
        color = default;
        try
        {
            if (string.IsNullOrWhiteSpace(text) || ColorConverter.ConvertFromString(text) is not System.Windows.Media.Color c) return false;
            color = c;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // ---- numbers -----------------------------------------------------------------------

    private static bool TryNumber(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Plural(int n) => n == 1 ? "" : "s";

    private static string UnitName(MeasureUnit unit) => unit switch
    {
        MeasureUnit.Inch => "in",
        MeasureUnit.Pixel => "px",
        _ => "cm",
    };
}
