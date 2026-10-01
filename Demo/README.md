# Samples

Five small projects, each the smallest thing that shows one way of using
BluePrint.

The three report samples share one report definition, [`nw-05-invoice.bpt`](../NorthwindBpt/nw-05-invoice.bpt):
an invoice for a single order, picked by the order number the report declares as
a retrieval argument. It reads the SQLite copy of Northwind in
[`NorthwindDB`](../NorthwindDB), a single file already in this repository, so there
is nothing to install or start. `Demo.Render` fills it four ways, one block of
`Program.cs` each; `Demo.Preview` puts the same four on buttons. Every block and
every button does the whole job by itself, so each way can be read on its own.
The two barcode samples share one list of
values, `BarcodeSamples.cs`, so the page one writes and the window the other
shows always hold the same symbols.

| Project | Shows |
| --- | --- |
| `Demo.Render` | load a `.bpt`, fill it four different ways, export each to PDF, with no UI at all |
| `Demo.Preview` | `BluePrintPreviewControl` in a WPF window, with those four ways on buttons |
| `Demo.Designer` | `BluePrintDesignerControl` embedded in a host application, with Retrieve wired to the same database |
| `Demo.Barcode` | every symbology written out as SVG and PNG, plus one page showing them all |
| `Demo.Barcode.Wpf` | the same symbologies on screen, re-encoded as you type, drawn by WPF |

## Running them

```
dotnet run --project Demo.Render
dotnet run --project Demo.Preview
dotnet run --project Demo.Designer
dotnet run --project Demo.Barcode
dotnet run --project Demo.Barcode.Wpf
```

`Demo.Render` writes one PDF next to its executable for each of the four ways
of getting the rows described below, and prints what each one did.
`Demo.Barcode` writes a `barcodes` folder next to its own, holding an SVG and a
PNG for each symbology and an `index.html` that shows them together. The rest
open a window.

`Demo.Preview`, `Demo.Designer` and `Demo.Barcode.Wpf` are WPF applications and
need Windows. `Demo.Render` and `Demo.Barcode` do not.

## Package versions

Each project references its package from nuget.org by an exact version, so what
you build here is what a published package gives you. To try a newer release,
change the `Version` in the `.csproj`.

## Where the pieces come from

| Package | Pulled in by |
| --- | --- |
| `BlueCrafts.BluePrint.Barcode` | `Demo.Barcode` and `Demo.Barcode.Wpf` directly |
| `BlueCrafts.BluePrint.Render` | `Demo.Render` directly, and it brings the barcode package |
| `BlueCrafts.BluePrint.Render.WPF` | `Demo.Preview` directly, and it brings `BlueCrafts.BluePrint.Render` |
| `BlueCrafts.BluePrint.Designer` | `Demo.Designer` directly, and it brings the whole chain |
| `Microsoft.Data.Sqlite` | `Demo.Render` and `Demo.Preview` directly - see below |

Installing the one BlueCrafts package that matches what you are building is
enough; the rest of that chain comes with it.

## The three report samples

No BlueCrafts package references a database driver, and none of them opens a
connection. A report definition carries its query; the host runs it and hands
the rows back. That is why `Demo.Render`, `Demo.Preview` and `Demo.Designer`
reference `Microsoft.Data.Sqlite` themselves, and it is the same everywhere: point a
report at Oracle and the driver is the host's to choose.

BluePrint is BluePrintDocument-centric: one `BluePrintDocument` is the centre of
everything. Create it, load a definition into it, put the rows in it, then export
it or show it. With a connection, that is the whole of it:

```csharp
var report = new BluePrintDocument();
report.LoadBpt(File.ReadAllText("nw-05-invoice.bpt"));
await report.RetrieveAsync(new SqliteConnection(connectionString),
                           new Dictionary<string, object?> { ["OrderId"] = 11077L });
report.ExportToPdf("invoice.pdf");      // or, in a window: Preview.ReportDocument = report;
```

`LoadBpt` takes the content of a `.bpt`, not its path, so the definition can
come from a file, a database, an HTTP response or an embedded resource alike -
reading it is the host's job. `new BluePrintDocument(bpt)` does both steps at
once.

`RetrieveAsync` runs the query the `.bpt` carries - the main query and every
lookup query - and the report keeps the rows it got, together with the argument
values it retrieved with, so any expression in the report that prints
`@OrderId` sees the same value the query used. Export and preview then use what
the report holds, so there is nothing more to pass.

`Demo.Designer` opens the same definition. Editing a report needs no
connection; the data source page shows the query as text. Rows on the design
surface are the one thing the host wires up itself, because the designer never
opens a connection. The designer asks for `@OrderId`, then hands the host an
empty `BluePrintDocument` for the report being designed, and the host fills it
the same way as above:

```csharp
Designer.RetrieveReportFactory = async (report, args) =>
{
    await using var connection = new SqliteConnection(connectionString);
    await report.RetrieveAsync(connection, args);
    return true;      // false (or an exception) keeps the rows of the last retrieve
};
```

### Four ways to fill the report

Where those rows come from is the host's decision, and there is more than one
reasonable answer. `Demo.Preview` puts all four on buttons across the top of
the window, and `Demo.Render`, which has nothing to click, runs the same four
one after another and writes a PDF for each. Each button handler in
`MainWindow.xaml.cs`, and each block in `Program.cs`, does the whole job by
itself - load the report, put the rows in it, show or export it - so reading one
is enough to copy that way into your own code.

| Way | What it does |
| --- | --- |
| **Own DbConnection** | `report.RetrieveAsync(connection, args)` runs the query the `.bpt` carries on a connection the host gives it - the main query and every lookup query, `{bp:...}` calls and `@arguments` included. A closed connection is opened for the retrieve and closed again; an open one is used and left open, which is the shape for an application that already holds a connection or a transaction (`new DbQueryOptions { Transaction = tx }`). Nothing to register: BluePrint works out the database from the connection. |
| **Connection Profile** | The host describes the database in a `ConnectionProfile` and registers the driver it references; `report.RetrieveAsync(profile, args)` opens a connection, runs the query the `.bpt` carries, and closes it again. That query contains `{bp:...}` calls - the things that let one report run on all six supported databases - and they are rendered for whatever the profile points at. Nothing in the sample knows or cares that the answer happens to be SQLite. |
| **Own DbConnection + SetSql** | As the first, but `report.SetSql(...)` first replaces the query with SQLite SQL of our own, and the report still runs it. Use this shape when the host builds the statement itself. `SetSql` does not rebuild the report's columns, so the replacement has to return the same ones in the same order. |
| **Own Query + List** | No BluePrint data layer at all: the sample queries with `Microsoft.Data.Sqlite` directly - it could as well be Entity Framework or Dapper - and hands over a `List<InvoiceLine>` with `report.SetData(lines)`. Properties are matched to the report's column names, ignoring case; `report.SetArgumentValues(args)` supplies `@OrderId` to any expression that prints it, as a retrieve would. Do not run the `.bpt`'s own query this way: its `{bp:...}` calls are only translated when the report retrieves. |

A property is matched to the name of the report's column, not to the name in the
database. This report was built from a query over several tables, so a column
taken straight from a table carries the table's name - `Products_ProductName`,
not `ProductName` - and `InvoiceLine` is written that way. Studio's Column List
shows a report's column names.

All four end with the same rows, and the four PDFs `Demo.Render` writes come out
byte for byte identical apart from the document id PDF gives each file. That is
the point worth taking away: the engine takes rows, and how they were fetched is
not its business.

The report keeps its rows once it has them, so paging, zoom and printing in
`Demo.Preview` never go back to the database. Retrieve again, and set
`Preview.ReportDocument = report` again, to show new rows.

## The two barcode samples

Both start from the same two calls, and everything else is presentation:

```csharp
var result = BarcodeEncoders.Default.Encode(BarcodeSymbology.EAN13, "590123412345");
var layout = BarcodeLayoutBuilder.Fit(result.Symbol!, width, height);
```

`Encode` returns a grid of modules with no unit attached to it, and a status
saying why when it cannot: a bad check digit, a character the symbology does not
carry, more data than it holds. `Fit` places that grid in the space available,
and returns null rather than draw modules too small to scan.

What each sample adds on top is worth comparing:

| | How the symbol reaches the screen or the file |
| --- | --- |
| `Demo.Barcode` | `BarcodeSvgWriter` for SVG, and `SkiaBarcodeRenderer` for PNG |
| `Demo.Barcode.Wpf` | `BarcodeView.cs`, about a hundred lines that draw the grid as WPF geometry |

`BarcodeView.cs` is the file to copy if you want barcodes in your own WPF
application. It stays vector all the way to the screen and to the printer, and
it handles the two things that are easy to get wrong: filling the quiet zone
with the paper colour, and showing EAN and UPC digits in their required groups
with the guard bars reaching down past them.
