# Showcase

Complete applications built on BluePrint, larger than the samples in
[`Demo`](../Demo): each one solves a whole task from start to finish, the way
an application of your own would.

| Application | Shows |
| --- | --- |
| [`LabelPrinting`](LabelPrinting) | design a label with `BluePrintDesignerControl`, fill it from a data file, and print it straight to a label printer as a 1-bit image in the printer's own command language, with no printer driver in between |

Like the samples, each application references the BlueCrafts packages from
nuget.org by an exact version, so what you build here is what the published
packages give you.

LabelPrinting is a WPF application and needs Windows.

```
dotnet run --project LabelPrinting
```

Each application has a README of its own that walks through it.
