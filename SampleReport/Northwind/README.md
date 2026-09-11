# Northwind Reports

Twelve reports built on the classic **Northwind** sample database, running on **SQLite** - a single
file, no server, nothing to install.

These cover the things the [external-data samples](../README.md) cannot: real queries, retrieval
arguments, lookup datasets, and subbands.

---

## Setup, once

| | |
|---|---|
| **1** | Copy the `DataProvider` folder from the release next to `BluePrint.Studio.exe` |
| **2** | Start Studio, and check **Database > Data Provider Library...** shows **SQLite** as ready |
| **3** | **Database > Database Profile...** and add a profile pointing at `northwind.db` |

The connection string is just the path to the file:

```
Data Source=path\to\SampleReport\Northwind\northwind.db
```

After that, open any report and press **Retrieve**.

> The `DataProvider` folder holds the SQLite driver. BluePrint ships no database driver inside the
> program - this folder is how you add one, and the same mechanism works for every other supported
> database. See **Connecting to a Database** in the BluePrint manual.

---

## The nine standard Northwind reports

| Report | Shows |
|---|---|
| [`nw-01-alphabetical-list-of-products.bpt`](nw-01-alphabetical-list-of-products.bpt) | A plain list with a three-table join, sorted by name |
| [`nw-02-catalog.bpt`](nw-02-catalog.bpt) | A catalogue: one page per category, with the **category picture from a database blob** |
| [`nw-03-customer-labels.bpt`](nw-03-customer-labels.bpt) | **Label report** - mailing labels, two across |
| [`nw-04-employee-sales-by-country.bpt`](nw-04-employee-sales-by-country.bpt) | **Two levels of grouping** with subtotals at each |
| [`nw-05-invoice.bpt`](nw-05-invoice.bpt) | A full invoice for one order, chosen by a **retrieval argument**, with a barcode |
| [`nw-06-products-by-category.bpt`](nw-06-products-by-category.bpt) | Stock levels, with **colours from an expression** on anything at or below its reorder level |
| [`nw-07-sales-by-category.bpt`](nw-07-sales-by-category.bpt) | Sales per product with each one's **share of its group total** |
| [`nw-08-sales-by-year.bpt`](nw-08-sales-by-year.bpt) | **Crosstab** - categories down the side, year and quarter across the top |
| [`nw-09-ten-most-expensive-products.bpt`](nw-09-ten-most-expensive-products.bpt) | A top-ten list, ranked with `Row()` |

## Three that show what BluePrint adds

| Report | Shows |
|---|---|
| [`nw-10-orders-with-lines.bpt`](nw-10-orders-with-lines.bpt) | **Subband** - each order prints its own lines underneath, from a second query |
| [`nw-11-lookup-functions.bpt`](nw-11-lookup-functions.bpt) | **`Lookup`, `LookupSet` and `Join`** - values pulled from other queries by id |
| [`nw-12-sales-dashboard.bpt`](nw-12-sales-dashboard.bpt) | Column, line, and pie **charts** over one set of rows |

---

## Running the invoice

`nw-05-invoice.bpt` takes an order number. Press **Retrieve** and you will be asked for it. Any
number from **10248 to 11077** works; try **10250**.

That is what a retrieval argument is for: one template, one order per run, no editing.

## Which report shows which feature

| You want to see | Open |
|---|---|
| A simple query and a plain list | 01 |
| Images stored in the database | 02 |
| A label sheet | 03 |
| Nested groups and subtotals | 04 |
| A parameter entered at run time | 05 |
| Colours that follow the data | 06 |
| A percentage of a group total | 07 |
| A crosstab | 08 |
| Ranking and row numbers | 09 |
| A child table under each row | 10 |
| Reading a second query by key | 11 |
| Charts | 12 |

---

## The database

`northwind.db` is the classic Northwind sample, converted to SQLite by the
[northwind-SQLite3](https://github.com/jpwhite3/northwind-SQLite3) project and distributed here
under its MIT licence. It has been trimmed back to the original data - 830 orders and 2,155 order
lines - so the file stays small and the reports stay readable.

| Table | Rows |
|---|--:|
| Orders | 830 |
| Order Details | 2,155 |
| Customers | 93 |
| Products | 77 |
| Employees | 9 |
| Suppliers | 29 |
| Categories | 8 |
| Shippers | 3 |

**Two things to know about the data**

- **Dates are stored as text**, in `yyyy-MM-dd` form, which is how SQLite keeps dates. The reports
  show them as they are. To format one, convert it first.
- **Customer names contain accented letters** - `Berglunds snabbkop`, `Antonio Moreno Taqueria`,
  `Koniglich Essen`. That is deliberate: it shows that text measurement and font handling are
  working, which a report full of plain ASCII would not.

## The queries

Every report carries its own SQL, which you can read and change in **Design > Data Source**. They
are written to be readable rather than clever, and they use only ordinary SQL - no SQLite-specific
features except the date functions in report 08.

### Why every money column is cast

```sql
CAST(p.UnitPrice AS REAL) AS UnitPrice
```

**SQLite decides a value's type row by row, not column by column.** A price of exactly `18` comes
back as a whole number while `18.50` comes back as a decimal - from the same column, in the same
query. A report binds to a column of one declared type, so without the cast the type it was designed
against depends on which row happened to come first.

The cast belongs in the **query**, which is where the column list comes from. If you write your own
SQL against SQLite, do the same for anything you will format as money or use in a total. Other
databases declare their types properly and do not need it.

---

## Two settings worth finding

### Colours from an expression - report 06

The red cells in **Products by Category** are not a fixed colour. Select the *In stock* column and
look at **Text colour** and **Fill colour** in the property panel: each has an `[fx]` button beside
it, and each holds an expression rather than a colour.

```
Text colour   Iif(UnitsInStock <= ReorderLevel, "#B00000", "#000000")
Fill colour   Iif(UnitsInStock <= ReorderLevel, "#FFE0E0", "Transparent")
```

The expression is worked out for every row, so the colour follows the data.

Most settings in the panel have that `[fx]` button. The expression has to produce a value of the
right kind - a colour here, `true` or `false` for *Visible only when...* - and if it cannot, the
setting keeps its fixed value and the problem is listed in Verify.

### The chart's category limit - report 12

A chart limits how many categories it draws, and a pie chart's limit is lower than the others.
Northwind has eight product categories, one more than a pie shows by default, so the eighth would
be merged into a single leftover slice.

Report 12 raises the limit so all eight appear. The setting is in the chart's data reduction
options.

> If a chart of yours reports *"showing N of M categories"*, this is why. Raise the limit, or accept
> the merge and name the leftover slice something meaningful.

---

## Using these from code

```csharp
using BluePrint.DataSources;
using BluePrint.Render;

BpStandardDrivers.Register(sqlite: Microsoft.Data.Sqlite.SqliteFactory.Instance);

var report = BluePrintDocument.Load(@"SampleReport\Northwind\nw-05-invoice.bpt");
report.OverrideConnection("Northwind", @"Data Source=SampleReport\Northwind\northwind.db");

report.ExportToPdf(@"out\invoice-10250.pdf", provider);
```

Your application references `Microsoft.Data.Sqlite` and `SQLitePCLRaw.bundle_e_sqlite3` as NuGet
packages - the `DataProvider` folder is only for Studio. See **Connecting to a Database** in the
BluePrint manual.
