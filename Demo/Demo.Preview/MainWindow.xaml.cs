// The preview control in a WPF window.
//
//   dotnet run --project Demo.Preview
//
// Setting Report is all it takes: the control renders the definition and shows
// the first page. Everything else on screen - paging, zoom, print - is the
// control's own.

using System.IO;
using System.Windows;
using BluePrint.Render;

namespace Demo.Preview;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var path = Path.Combine(AppContext.BaseDirectory, "HelloReport.bpt");
        Preview.Report = BluePrintDocument.Load(path).Definition;

        // With data, hand the control a provider as well and it re-renders:
        //
        //     Preview.DataProvider = provider;
    }
}
