// Smallest thing that renders a BluePrint report with real data.
//
//   dotnet run --project Demo.Render
//
// Loads nw-05-invoice.bpt, fills it from the Northwind file in this repository,
// and writes a PDF next to the executable. The report is one invoice, chosen by
// the order number it declares as a retrieval argument.
//
// Demo.Preview shows the same three ways to fill it as three buttons. Here,
// with no UI to click, all three run one after another and each writes its own
// PDF - which is also the point: the three are interchangeable, and the three
// files coming out identical is what says so.

using BluePrint.Render;
using Demo.Shared;

foreach (var way in Enum.GetValues<RetrieveWay>())
{
    // A fresh document per way. SetSqlThenRetrieve changes the query the
    // definition carries, so reusing one instance would let that leak into
    // whatever ran next.
    var report = BluePrintDocument.Load(InvoiceQuery.ReportPath);

    var rows = await InvoiceQuery.LoadAsync(way, report);

    // The rows, and the argument value they were retrieved with. The report
    // needs both: the detail lines come from the rows, and the heading prints
    // @OrderId.
    report.SetExternalData(rows);
    report.SetArgumentValues(InvoiceQuery.ArgumentValues);

    var outputPath = Path.Combine(AppContext.BaseDirectory, $"Invoice-{way}.pdf");
    report.ExportToPdf(outputPath);

    Console.WriteLine($"{way,-24} {rows.Rows.Count,3} rows  ->  {Path.GetFileName(outputPath)}");
    Console.WriteLine($"{"",-24}     {InvoiceQuery.Describe(way)}");
}

Console.WriteLine();
Console.WriteLine($"Written to {AppContext.BaseDirectory}");

// ExportToExcel, ExportToHtml, ExportToCsv and ExportToImage take the same
// arguments as ExportToPdf.
//
// To write into a stream instead of a file, for example from a web service:
//
//     report.ExportToPdf(responseStream, ct: cancellationToken);
