# Samples

Five small projects, each the smallest thing that shows one way of using
BluePrint.

The three report samples share one report definition, [`nw-05-invoice.bpt`](../NorthwindBpt/nw-05-invoice.bpt):
an invoice for a single order, picked by the order number the report declares as
a retrieval argument. It reads the SQLite copy of Northwind in
[`NorthwindDB`](../NorthwindDB), a single file already in this repository, so there
is nothing to install or start. `Demo.Render` and `Demo.Preview` also share the
query that fills it, `InvoiceQuery.cs`. The two barcode samples share one list of
values, `BarcodeSamples.cs`, so the page one writes and the window the other
shows always hold the same symbols.

| Project | Shows |
| --- | --- |
| `Demo.Render` | load a `.bpt`, fill it three different ways, export each to PDF, with no UI at all |
| `Demo.Preview` | `BluePrintPreviewControl` in a WPF window, with those three ways on buttons |
| `Demo.Designer` | `BluePrintDesignerControl` embedded in a host application |
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

`Demo.Render` writes one PDF next to its executable for each of the three ways
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
the rows back. That is why `Demo.Render` and `Demo.Preview` reference
`Microsoft.Data.Sqlite` themselves, and it is the same everywhere: point a
report at Oracle and the driver is the host's to choose.

Once `InvoiceQuery.cs` has the rows, that is the whole of it:

```csharp
var report = BluePrintDocument.Load(InvoiceQuery.ReportPath);
report.SetExternalData(rows);
report.SetArgumentValues(new Dictionary<string, object?> { ["OrderId"] = 11077L });
report.ExportToPdf(outputPath);
```

The argument values go in beside the rows because the report prints them: an
invoice heading that says which order this is reads `@OrderId`, and it has no
other way to know what was retrieved.

`Demo.Designer` opens the same definition and reads no data at all: editing a
report needs no connection.

### Three ways to get the rows

Where those rows come from is the host's decision, and there is more than one
reasonable answer. `Demo.Preview` puts all three on buttons across the top of
the window, and `Demo.Render`, which has nothing to click, runs all three and
writes a PDF for each. They are written once, in `InvoiceQuery.cs`.

| | What it does |
| --- | --- |
| **Retrieve** | Runs the query the `.bpt` already carries, as it is written. That query contains `{bp:...}` calls - the things that let one report run on all six supported databases - and `ReportQueryService` renders them for whatever the connection points at. Nothing in the sample knows or cares that the answer happens to be SQLite. |
| **Set SQL query before retrieve** | `report.SetSql(...)` replaces the query with SQLite SQL of our own, and BluePrint still runs it. Use this shape when the host builds the statement itself. `SetSql` does not rebuild the report's columns, so the replacement has to return the same ones in the same order. |
| **Set DataTable before retrieve** | No BluePrint data layer at all: the sample queries with `Microsoft.Data.Sqlite` directly and hands over a `DataTable`. This is the shape for a host that already has its own data access, or whose rows never came from a database in the first place. |

All three end in the same `SetExternalData`, and the three PDFs `Demo.Render`
writes come out byte for byte identical apart from the document id PDF gives
each file. That is the point worth taking away: the engine takes rows, and how
they were fetched is not its business.

`Demo.Preview` wraps the rows in a `CachedDataProvider` before handing them to
the control, so paging and zoom never go back to the database.

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
