# Samples

Three small projects, each the smallest thing that shows one way of using
BluePrint. They share one report definition, `HelloReport.bpt`: a single line of
text in the report header, with no data source, so nothing here depends on a
database being available.

| Project | Shows |
| --- | --- |
| `Demo.Render` | load a `.bpt` and export it to PDF, with no UI at all |
| `Demo.Preview` | `BluePrintPreviewControl` in a WPF window |
| `Demo.Designer` | `BluePrintDesignerControl` embedded in a host application |

## Running them

```
dotnet run --project Demo.Render
dotnet run --project Demo.Preview
dotnet run --project Demo.Designer
```

`Demo.Render` writes `HelloReport.pdf` next to its executable and prints the
path. The other two open a window.

`Demo.Preview` and `Demo.Designer` are WPF applications and need Windows.
`Demo.Render` does not.

## Package versions

Each project references its package from nuget.org by an exact version, so what
you build here is what a published package gives you. To try a newer release,
change the `Version` in the `.csproj`.

## Where the pieces come from

| Package | Pulled in by |
| --- | --- |
| `BlueCrafts.BluePrint.Render` | `Demo.Render` directly |
| `BlueCrafts.BluePrint.Render.WPF` | `Demo.Preview` directly, and it brings `BlueCrafts.BluePrint.Render` |
| `BlueCrafts.BluePrint.Designer` | `Demo.Designer` directly, and it brings the whole chain |

Installing the one package that matches what you are building is enough; the
rest of the chain comes with it.
