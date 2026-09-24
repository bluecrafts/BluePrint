// The report designer embedded in a host application.
//
//   dotnet run --project Demo.Designer
//
// Setting Report opens the definition on the design surface. Read it back from
// the same property to save whatever the user edited.
//
// This opens nw-05-invoice.bpt, the same report the other two samples render,
// so the bands, the expressions and the query behind it can be looked at from
// the inside. Editing needs no database: the data source page shows the query
// as text, and retrieving rows is the one thing a host has to wire up itself.

using System.IO;
using System.Windows;
using BluePrint.Render;

namespace Demo.Designer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var path = Path.Combine(AppContext.BaseDirectory, "nw-05-invoice.bpt");
        Designer.Report = BluePrintDocument.Load(path).Definition;
    }
}
