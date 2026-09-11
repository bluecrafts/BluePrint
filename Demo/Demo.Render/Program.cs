// Smallest thing that renders a BluePrint report.
//
//   dotnet run --project Demo.Render
//
// Loads a .bpt definition and writes a PDF next to the executable. No database
// and no data provider: the report is one label in the report header, which is
// enough to show the load-then-export shape that every other use starts from.

using BluePrint.Render;

var definitionPath = Path.Combine(AppContext.BaseDirectory, "HelloReport.bpt");
var outputPath = Path.Combine(AppContext.BaseDirectory, "HelloReport.pdf");

var report = BluePrintDocument.Load(definitionPath);
report.ExportToPdf(outputPath);

Console.WriteLine($"Wrote {outputPath}");

// With data, the shape is the same. Point the report at a connection the host
// has registered, then export:
//
//     report.OverrideConnection("MainDB", connectionString);
//     report.ExportToPdf(outputPath);
//
// To write into a stream instead of a file, for example from a web service:
//
//     report.ExportToPdf(responseStream, provider, ct: cancellationToken);
