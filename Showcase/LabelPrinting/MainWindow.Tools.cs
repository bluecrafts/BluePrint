// The element tools, in a vertical toolbar on the left of the design surface.
// The designer's own tool box is hidden (ShowElementToolBar="False"); these
// buttons run the very same designer
// commands ("tool.label", "tool.field" ...), so placing an element works
// exactly as in the BluePrint designer. Chart and Sub Band are left out: a
// black and white label has no use for them.

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LabelPrinting;

public partial class MainWindow
{
    /// <summary>A designer command, the icon of its button, and its tooltip.</summary>
    private sealed record ElementTool(string CommandId, string IconKey, string Name, string Hint);

    private static readonly ElementTool[] ElementTools =
    [
        new("tool.arrow",    "Icon.Select",  "Select",  "Select and move elements (Esc)"),
        new("tool.label",    "Icon.Text",    "Text",    "Fixed text"),
        new("tool.field",    "Icon.Field",   "Field",   "A column of the label data"),
        new("tool.computed", "Icon.Compute", "Compute", "A value worked out from an expression"),
        new("tool.image",    "Icon.Picture", "Picture", "A picture from a file"),
        new("tool.line",     "Icon.Line",    "Line",    "A straight line"),
        new("tool.shape",    "Icon.Shape",   "Shape",   "A rectangle or an ellipse"),
        new("tool.barcode",  "Icon.Barcode", "Barcode", "A barcode or QR code"),
    ];

    /// <summary>The ids of the tools in the toolbar, in order. The tests read it.</summary>
    public static IReadOnlyList<string> ElementToolIds => ElementTools.Select(t => t.CommandId).ToArray();

    private void InitializeElementTools()
    {
        foreach (var tool in ElementTools)
            ElementsToolBar.Items.Add(BuildToolButton(tool));

        // Esc while a tool button has the focus (after clicking it): the key does
        // not reach the design surface, so go back to Select here.
        ElementsToolBar.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || Designer.ActiveTool == "tool.arrow") return;
            Designer.ActiveTool = "tool.arrow";
            e.Handled = true;
        };

        UpdateElementTools();
    }

    private ToggleButton BuildToolButton(ElementTool tool)
    {
        var command = Designer.Commands.Get(tool.CommandId);
        var button = new ToggleButton
        {
            Command = command,
            Content = new Path { Data = (Geometry)FindResource(tool.IconKey), Style = (Style)FindResource("Bp.ToolIcon") },
            // No Style here: the toolbar gives every ToggleButton it holds the toggle
            // style of the tray (MainWindow.xaml), as it does for the buttons.
            ToolTip = $"{tool.Name}: {tool.Hint}",
        };
        AutomationProperties.SetName(button, tool.Name);
        ToolTipService.SetShowOnDisabled(button, true);

        // WPF does not keep IsChecked in step with the command; the designer raises
        // CanExecuteChanged on every command whenever the active tool changes.
        button.IsChecked = command.IsChecked;
        command.CanExecuteChanged += (_, _) => button.IsChecked = command.IsChecked;

        // A click on the tool that is already chosen keeps it chosen; Select, Esc
        // or placing the element end it.
        button.Unchecked += (_, _) =>
        {
            if (command.IsChecked) button.IsChecked = true;
        };
        return button;
    }

    /// <summary>The tools place elements on the design surface: only with a label open, in the Design tab.</summary>
    private void UpdateElementTools()
    {
        if (ElementsToolBar is null) return;   // still building the window
        var usable = Designer.Report is not null && DesignTab.IsSelected;
        ElementsToolBar.IsEnabled = usable;
        if (!usable && Designer.Report is not null && Designer.ActiveTool != "tool.arrow")
            Designer.ActiveTool = "tool.arrow";
    }
}
