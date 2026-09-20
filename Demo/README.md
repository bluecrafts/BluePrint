# Samples

Five small projects, each the smallest thing that shows one way of using
BluePrint.

The three report samples share one report definition, `HelloReport.bpt`: a
single line of text in the report header, with no data source, so nothing there
depends on a database being available. The two barcode samples share one list of
values, `BarcodeSamples.cs`, so the page one writes and the window the other
shows always hold the same symbols.

| Project | Shows |
| --- | --- |
| `Demo.Render` | load a `.bpt` and export it to PDF, with no UI at all |
| `Demo.Preview` | `BluePrintPreviewControl` in a WPF window |
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

`Demo.Render` writes `HelloReport.pdf` next to its executable and prints the
path. `Demo.Barcode` writes a `barcodes` folder next to its own, holding an SVG
and a PNG for each symbology and an `index.html` that shows them together. The
rest open a window.

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

Installing the one package that matches what you are building is enough; the
rest of the chain comes with it.

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
