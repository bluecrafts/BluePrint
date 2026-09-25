# Northwind Reports

Twenty-nine reports built on the classic **Northwind** sample database, running on **SQLite** - a
single file, no server, nothing to install.

These cover the things the [external-data samples](../SampleReport/README.md) cannot: real queries, retrieval
arguments, lookup datasets, and subbands.

---

## Setup, once

| | |
|---|---|
| **1** | Copy the `DataProvider` folder from the release next to `BluePrint.Studio.exe` |
| **2** | Start Studio, and check **Database > Database Profile...** shows **SQLite** with its driver name, not *unavailable* |
| **3** | **Database > Database Profile...** and add a profile pointing at `NorthwindDB/SQLite/northwind.db` |

The connection string is just the path to the file:

```
Data Source=path\to\NorthwindDB\SQLite\northwind.db
```

After that, open any report and press **Retrieve**.

> The `DataProvider` folder holds the SQLite driver. BluePrint ships no database driver inside the
> program - this folder is how you add one, and the same mechanism works for every other supported
> database. See **Connecting to a Database** in the BluePrint manual.

---

## The twelve standard Northwind reports

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
| [`nw-14-sales-totals-by-amount.bpt`](nw-14-sales-totals-by-amount.bpt) | Large orders sorted by value, split into **bands the query works out per row** |
| [`nw-15-summary-of-sales-by-quarter.bpt`](nw-15-summary-of-sales-by-quarter.bpt) | A summary with **no detail rows at all** - the detail band is there, at zero height |
| [`nw-16-summary-of-sales-by-year.bpt`](nw-16-summary-of-sales-by-year.bpt) | Quarters within years, with a **running total** and each line's share of the whole |

## Four that show what BluePrint adds

| Report | Shows |
|---|---|
| [`nw-10-orders-with-lines.bpt`](nw-10-orders-with-lines.bpt) | **Subband** - each order prints its own lines underneath, from a second query |
| [`nw-11-lookup-functions.bpt`](nw-11-lookup-functions.bpt) | **`Lookup`, `LookupSet` and `Join`** - values pulled from other queries by id |
| [`nw-12-sales-dashboard.bpt`](nw-12-sales-dashboard.bpt) | Column, line, and pie **charts** over one set of rows |
| [`nw-13-employee-directory.bpt`](nw-13-employee-directory.bpt) | **Photos from the database** on an image element, text that grows with its content, and portable functions throughout |

## Eight more, for the things a real report ends up needing

| Report | Shows |
|---|---|
| [`nw-17-monthly-sales-by-employee.bpt`](nw-17-monthly-sales-by-employee.bpt) | A **date range entered at run time**, and a **stacked column chart** of the same rows |
| [`nw-18-shipping-performance.bpt`](nw-18-shipping-performance.bpt) | Late and unshipped orders, where a **missing date stays missing** instead of counting as zero |
| [`nw-19-customer-statement.bpt`](nw-19-customer-statement.bpt) | **One page per customer**, with a running balance down their orders |
| [`nw-20-product-sales-abc.bpt`](nw-20-product-sales-abc.bpt) | Products ranked by sales, with a **cumulative percentage** and an A/B/C grade |
| [`nw-21-employee-territories.bpt`](nw-21-employee-territories.bpt) | Staff by region, each with the **list of territories read from a second query** |
| [`nw-22-reorder-list-by-supplier.bpt`](nw-22-reorder-list-by-supplier.bpt) | Stock to reorder, with a **QR code** of the supplier's contact card and a drawn frame |
| [`nw-23-customer-address-book.bpt`](nw-23-customer-address-book.bpt) | Customers in alphabetical sections, **grouped on a letter the query works out** |
| [`nw-24-sales-by-country-crosstab.bpt`](nw-24-sales-by-country-crosstab.bpt) | A second crosstab: countries down the side, years across the top |

## Five that fill in the rest of what BluePrint draws

| Report | Shows |
|---|---|
| [`nw-25-chart-gallery.bpt`](nw-25-chart-gallery.bpt) | **Area, scatter, stacked bar and 100 per cent stacked** over one set of rows |
| [`nw-26-barcode-symbologies.bpt`](nw-26-barcode-symbologies.bpt) | The same code as **Code128, DataMatrix, PDF417 and Aztec**, side by side |
| [`nw-27-client-side-filter.bpt`](nw-27-client-side-filter.bpt) | A query with **no WHERE clause** - the report decides which rows to keep |
| [`nw-28-sales-and-orders-crosstab.bpt`](nw-28-sales-and-orders-crosstab.bpt) | A crosstab with **two measures in every cell** |
| [`nw-29-order-settlement-slip.bpt`](nw-29-order-settlement-slip.bpt) | **Slots** - the goods on the left and the discounts on the right, from one query, line beside line |

---

## Running the invoice

`nw-05-invoice.bpt` takes an order number. Press **Retrieve** and you will be asked for it. Any
number from **10248 to 11077** works; try **10250**.

That is what a retrieval argument is for: one template, one order per run, no editing.

Two other reports ask for something when you retrieve them:

| Report | Asks for | Try |
|---|---|---|
| `nw-17-monthly-sales-by-employee.bpt` | a date range | **1997-01-01** to **1997-12-31** |
| `nw-19-customer-statement.bpt` | a country | **France** (eleven customers), or **Germany**, **USA**, **Brazil** |

Report 17 compares dates as `yyyy-MM-dd` text on both sides of the comparison, which is why the last
day of the range is included rather than dropped at midnight.

## Which report shows which feature

| You want to see | Open |
|---|---|
| A simple query and a plain list | 01 |
| Images stored in the database | 02, 13 |
| A label sheet | 03 |
| Nested groups and subtotals | 04 |
| A parameter entered at run time | 05 |
| Colours that follow the data | 06 |
| A percentage of a group total | 07 |
| A crosstab | 08 |
| Ranking and row numbers | 09 |
| Rows sorted into bands | 14 |
| A summary with the detail rows hidden | 15 |
| A running total, and a share of the whole | 16 |
| A child table under each row | 10 |
| Reading a second query by key | 11 |
| Charts | 12, 17, 20 |
| One report for every database | 13 |
| A date range asked for at run time | 17 |
| Missing values that must not read as zero | 18 |
| A page break per group | 19 |
| A running total and a cumulative percentage | 16, 19, 20 |
| A list gathered from another query | 21 |
| A QR code, and a drawn frame | 22 |
| Sections from the first letter of a name | 23 |
| Every chart type BluePrint draws | 12, 17, 25 |
| 2D barcodes | 22, 26 |
| Filtering in the report instead of the query | 27 |
| Two measures in one crosstab cell | 28 |
| Two lists side by side in one band | 29 |

---

## The database

These reports run on [`NorthwindDB/SQLite/northwind.db`](../NorthwindDB/SQLite/northwind.db) -
the classic Northwind sample, built from **Microsoft's own install script** and distributed under
its MIT licence. See [`../NorthwindDB/`](../NorthwindDB/) for where it comes from and for the same
data as a script for five other databases.

| Table | Rows |
|---|--:|
| Orders | 830 |
| Order Details | 2,155 |
| Customers | 91 |
| Products | 77 |
| Employees | 9 |
| Suppliers | 29 |
| Categories | 8 |
| Shippers | 3 |

**Two things to know about the data**

- **Dates are stored as text**, in `yyyy-MM-dd HH:mm:ss` form, which is how SQLite keeps dates. Every
  time in Northwind is midnight, so reports 04, 05 and 10 select `{bp:DateText(o.OrderDate)}` and get
  `1996-07-04` - text, the same on every machine and every database. Comparing a date needs the same
  full form on both sides, or a comparison against a bare date will miss the last day of the range.
- **Customer names contain accented letters** - `Berglunds snabbköp`, `Antonio Moreno Taquería`,
  `Königlich Essen`. That is deliberate: it shows that text measurement and font handling are
  working, which a report full of plain ASCII would not.

### The same data on a real server

[`NorthwindDB/`](../NorthwindDB/) also holds the same thirteen tables and the same rows as a SQL
script for each database BluePrint supports - SQL Server, PostgreSQL, MySQL, Oracle and Firebird, as
well as SQLite. Load one into a server you already have if you would rather work against that than a
file. Each script was run against a real server of its product before being committed.

## The queries

Every report carries its own SQL, which you can read and change in **Design > Data Source**. They
are written to be readable rather than clever, and they use only ordinary SQL.

### One query, six databases - all thirteen reports

**Every report here runs unchanged on all six databases.** Nothing in the SQL belongs to one
product: the parts that differ from one database to the next are written as **portable functions**,
which BluePrint translates for whichever database the report is connected to - see
[Portable SQL Functions](https://github.com/bluecrafts/BluePrint/wiki/Reference-13-Portable-SQL-Functions)
in the manual.

```sql
SELECT {bp:Concat(e.FirstName, ' ', e.LastName)} AS EmployeeName,
       {bp:Year(e.HireDate)} AS HireYear, ...
FROM Employees e
LEFT JOIN Employees m ON m.EmployeeID = e.ReportsTo
```

Three of them appear again and again in these reports:

| In the query | Why |
|---|---|
| `{bp:Concat(a, ', ', b)}` | Joining text is `\|\|` on some databases, `+` on SQL Server, `CONCAT` on MySQL |
| `{bp:DateText(o.OrderDate)}` | A date printed as `1996-07-04` everywhere, rather than in the format of whatever machine prints it |
| `{bp:Name(Order Details)}` | The one table whose name has a space in it. Double quotes work everywhere except MySQL, back-ticks only on MySQL and SQLite |

Report 09 also uses `{bp:Limit(10)}` for its top ten, which becomes `TOP`, `LIMIT`, `ROWS` or
`FETCH FIRST` as needed.

`Concat` also behaves the same everywhere when a value is missing. Andrew Fuller reports to no one,
so his manager's name is null - and it stays null on Oracle too, where a plain `||` would have
turned it into an empty string and the report would print "Reports to " with nothing after it.

### Why report 14 groups on a number, not on a label

The bands in **Sales Totals by Amount** could have been written as text in the query:

```sql
CASE WHEN ... >= 10000 THEN '10,000 and over'
     WHEN ... >=  5000 THEN '5,000 to 9,999'
     ELSE                   '2,500 to 4,999' END AS AmountBand
```

That runs everywhere, but it does not print the same everywhere. **Firebird types a `CASE` of string
literals as `CHAR` of the longest branch and pads the shorter ones with spaces**, so the same group
heading comes back as `5,000 to 9,999 ` on Firebird and `5,000 to 9,999` on the others.

So the query returns `1`, `2` or `3` and the report turns the number into a heading:

```
Switch(AmountBand = 1, "10,000 and over", AmountBand = 2, "5,000 to 9,999", "2,500 to 4,999")
```

A number has no padding to disagree about. The wording now lives in the report, where you can change
it without touching SQL - which is the general rule: **keep in the query only what has to be in the
query**, and let the report do the rest. Report 08 gets away with `'Q1'`…`'Q4'` because every branch
is the same length.

### A column alias has to clear six sets of reserved words

Report 23 groups on the first letter of the company name, and that column was first called
`Initial`. Five databases were happy with it. **Oracle was not: `INITIAL` is a reserved word there**,
and the query failed with nothing more helpful than *FROM keyword not found where expected*.

It is now `InitialLetter`. When a report has to run on more than one database, an alias that reads a
little longer is cheaper than a word that one of them has already taken.

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

var report = BluePrintDocument.Load(@"NorthwindBpt\nw-05-invoice.bpt");
report.OverrideConnection("Northwind", @"Data Source=NorthwindDB\SQLite\northwind.db");

report.ExportToPdf(@"out\invoice-10250.pdf", provider);
```

Your application references `Microsoft.Data.Sqlite` and `SQLitePCLRaw.bundle_e_sqlite3` as NuGet
packages - the `DataProvider` folder is only for Studio. See **Connecting to a Database** in the
BluePrint manual.
