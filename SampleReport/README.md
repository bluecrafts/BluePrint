# Sample Reports

Two sets, for two different first questions.

| Set | Needs | Best for |
|---|---|---|
| **[These five](#what-is-here)** | Nothing at all | Your first hour. Open, import a file, print |
| **[Northwind](Northwind/)** | A one-off setup, still no server | Everything that needs a real query: parameters, lookups, subbands |

Start here. Move to Northwind when you want to see how a report works against a database.

---

## What is here

| Template | Report type | Shows |
|---|---|---|
| [`sales-summary.bpt`](Basic/sales-summary.bpt) | Canvas | Grouping, group totals, a grand total, page numbers |
| [`invoice.bpt`](Basic/invoice.bpt) | Canvas | A single document with lines, a barcode, VAT, and a signature block |
| [`product-labels.bpt`](Basic/product-labels.bpt) | **Label** | A sheet of labels, three across, each with a barcode |
| [`sales-by-region.bpt`](Basic/sales-by-region.bpt) | **Crosstab** | Regions down the side, quarters across the top, with totals |
| [`chart-showcase.bpt`](Basic/chart-showcase.bpt) | Canvas | Column, line, and pie charts over one set of rows |

All five are built on **External data**, which means the template carries the column list but no
query. You supply the rows.

---

## Opening one

1. Open Studio and open one of the `.bpt` files.
2. **Data > Retrieve**, and pick the matching file from [`data/`](Basic/data/).
3. **Preview > Preview**.

The report now shows real rows. Print it, or export it from **Preview > Export**.

---

## The data files

In [`data/`](Basic/data/), one file per template:

| File | For | Rows |
|---|---|---|
| `sales-summary.csv` | `sales-summary.bpt` | 56 |
| `sales-summary.json` | the same rows, as JSON | 56 |
| `sales-by-region.csv` | `sales-by-region.bpt` | 56 |
| `chart-showcase.csv` | `chart-showcase.bpt` | 56 |
| `invoice.csv` | `invoice.bpt` | 7 |
| `product-labels.csv` | `product-labels.bpt` | 9 |

`sales-summary` comes in **both CSV and JSON** so you can see the two shapes side by side. The
report does not care which you use.

### Formats you can import

| Format | Extension | Notes |
|---|---|---|
| **CSV** | `.csv` | Comma-separated, first row is the heading. The usual choice |
| **Text** | any | Any single character as the separator. Tab by default |
| **Excel** | `.xlsx` | Pick the sheet when you import |
| **JSON** | `.json` | An array of objects, matched to the report's columns by name |

The column **names** must match what the report expects; the **order** does not, for JSON. For CSV,
Text, and Excel the columns are read left to right.

---

## Using one from your own code

```csharp
using BluePrint.Render;

var report = BluePrintDocument.Load(@"SampleReport\Basic\sales-summary.bpt");

var row = report.Data.AppendRow();
row["Region"]       = "North";
row["CustomerName"] = "Acme Engineering";
row["OrderDate"]    = new DateTime(2026, 3, 4);
row["Amount"]       = 1250.00m;

report.ExportToPdf(@"out\sales-summary.pdf");
```

Or hand over a whole table at once with `report.SetExternalData(table)`.

---

## The data itself

Everything is invented. The companies, the product codes, and the numbers are made up, and the text
is English throughout so that the reports look the same wherever they are opened.

Amounts have two decimal places, dates are written `yyyy-MM-dd` in the files, and the reports format
them for display - which is the point of the display format settings you will see on the fields.

---

## Making your own

The quickest way to start is to copy the nearest sample and change it:

| You want | Start from |
|---|---|
| A list with subtotals | `sales-summary.bpt` |
| A single-document form | `invoice.bpt` |
| Labels or badges | `product-labels.bpt` |
| A matrix | `sales-by-region.bpt` |
| Anything with a chart | `chart-showcase.bpt` |

Change the columns in **Design > Data Source** to match your own data, and the elements follow.

---

## What these five do not cover

They are built on external data, so there is no query in them. That leaves out everything that only
makes sense against a database:

| | Where to see it |
|---|---|
| A real query, and the Data Source dialog | [Northwind 01](Northwind/) |
| A value entered when you retrieve | [Northwind 05](Northwind/) |
| **Lookup datasets and subbands** | [Northwind 10 and 11](Northwind/) |
| Images stored in the database | [Northwind 02](Northwind/) |
| Colours driven by an expression on live values | [Northwind 06](Northwind/) |

The [`Northwind/`](Northwind/) folder covers all of it, and still needs no database server - the
whole database is one file.
