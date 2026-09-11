// The report designer embedded in a host application.
//
//   dotnet run --project Demo.Designer
//
// Setting Report opens the definition on the design surface. Read it back from
// the same property to save whatever the user edited.

using System.IO;
using System.Windows;
using BluePrint.Render;

namespace Demo.Designer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var path = Path.Combine(AppContext.BaseDirectory, "HelloReport.bpt");
        Designer.Report = BluePrintDocument.Load(path).Definition;
    }
}
