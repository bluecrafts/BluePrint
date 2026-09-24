# The Northwind database

Microsoft's Northwind sample, for every database BluePrint supports. Two of them come ready to
open; the rest are a script you run into a server you already have.

Everything here is generated from [`source/instnwnd.sql`](source/instnwnd.sql) - **Microsoft's own
install script**, kept in the repository so the claim can be checked rather than taken on trust.
Nothing is edited by hand.

---

## What is here

| Ready to open | |
|---|---|
| [`SQLite/northwind.db`](SQLite/northwind.db) | No server at all. This is what the [sample reports](../NorthwindBpt/) use |
| [`Firebird/northwind50.fdb`](Firebird/northwind50.fdb) | For **Firebird 5** |
| [`Firebird/northwind25.fdb`](Firebird/northwind25.fdb) | For **Firebird 2.5** |

A Firebird database file carries the on-disk structure of the engine that made it, and an older
engine cannot open a newer file - hence two of them rather than one. Firebird 3 and 4 can open the
2.5 file; if you would rather have a native one, run the script.

Two things in the 2.5 file differ from the script, because the 2.5 engine will not accept them:
eight foreign-key names are shortened (2.5 allows 31 characters, ours run to 45), and the money
columns are `DECIMAL(18,4)` rather than `(19,4)` (2.5 tops out at 18 digits; the largest value in
Northwind is a freight charge of about 1,007). Every row is the same.

| Script | For |
|---|---|
| [`SQLite/northwind.sql`](SQLite/northwind.sql) | SQLite 3 |
| [`MSSQL/northwind.sql`](MSSQL/northwind.sql) | Microsoft SQL Server 2016 and later |
| [`PostgreSQL/northwind.sql`](PostgreSQL/northwind.sql) | PostgreSQL 12 and later |
| [`MySql/northwind.sql`](MySql/northwind.sql) | MySQL 8.0 and later, MariaDB 10.5 and later |
| [`Oracle/northwind.sql`](Oracle/northwind.sql) | Oracle Database 19c and later |
| [`Firebird/northwind.sql`](Firebird/northwind.sql) | Firebird 3.0 and later |

---

## What is in it

**13 tables, 3,308 rows, 16 views** - the same objects Microsoft's script creates, with the same
data, on all six.

### Tables

| Table | Rows | | Table | Rows |
|---|--:|---|---|--:|
| `Region` | 4 | | `Customers` | 91 |
| `Territories` | 53 | | `CustomerDemographics` | 0 |
| `Categories` | 8 | | `CustomerCustomerDemo` | 0 |
| `Suppliers` | 29 | | `Employees` | 9 |
| `Shippers` | 3 | | `EmployeeTerritories` | 49 |
| `Products` | 77 | | `Orders` | 830 |
| | | | `Order Details` | 2,155 |

`CustomerDemographics` and `CustomerCustomerDemo` are empty in Northwind itself. They are created so
the schema is complete.

Foreign keys are real constraints - deleting a category that still has products fails, on every one
of the six.

### Views

All sixteen, in dependency order (four of them read other views):

| | | |
|---|---|---|
| `Alphabetical list of products` | `Order Details Extended` | `Quarterly Orders` |
| `Category Sales for 1997` | `Order Subtotals` | `Sales by Category` |
| `Current Product List` | `Orders Qry` | `Sales Totals by Amount` |
| `Customer and Suppliers by City` | `Product Sales for 1997` | `Summary of Sales by Quarter` |
| `Invoices` | `Products Above Average Price` | `Summary of Sales by Year` |
| | `Products by Category` | |

Microsoft writes them in T-SQL, so four things are translated for the other five products:
`CONVERT(money, x)` becomes `CAST(x AS DECIMAL(19,4))`, `FirstName + ' ' + LastName` becomes `||` or
`CONCAT`, `'19971231'` becomes each product's own date literal, and the quoting changes. Every view
was then **run on a real server of each product and its row count compared against SQL Server** - all
sixteen agree, on all six.

### Images

`Categories.Picture` and `Employees.Photo` hold the real pictures from Microsoft's script, as plain
`.bmp` files that any image viewer - and a BluePrint image element - can open.

This is one of two places the data differs from the original (the other is `Discount`, under
**Column types**), and it is done by the generator, not by hand. In Microsoft's script every picture
is wrapped in an **OLE object**, a leftover from the desktop database Northwind started out in:
78 bytes of header (`Bitmap Image`, `Paint.Picture`) in front of the bitmap and a few bytes after it.
An ordinary image viewer cannot display that, so the generator takes the bitmap out and stores only
that. The pixels are unchanged.

This applies to **every copy here, the SQL Server script included** - none of them contains an OLE
object. There are 17 pictures: 8 in `Categories.Picture` and 9 in `Employees.Photo`.

| | SQLite | SQL Server | PostgreSQL | MySQL | Oracle | Firebird |
|---|---|---|---|---|---|---|
| Column type | `BLOB` | `VARBINARY(MAX)` | `BYTEA` | `LONGBLOB` | `BLOB` | `BLOB SUB_TYPE BINARY` |
| Written as | `X'...'` | `0x...` | `decode('...', 'hex')` | `0x...` | appended in chunks (below) | `x'...'` |

SQL Server's `image` type is deprecated, so the SQL Server script uses `VARBINARY(MAX)`, its
replacement.

Oracle is the odd one out: `HEXTORAW` takes at most 2000 characters and these images run to about
24,000, so each one is appended to the LOB in chunks by a short PL/SQL block after the inserts.

**Checked on a real server of each product**, and on both Firebird files: all 17 pictures start with
`BM`, their length matches the size the bitmap header declares, every one opens as an image, and the
bytes are identical in all six. Reports 02 and 13 in [`../NorthwindBpt/`](../NorthwindBpt/) show
them.

### Not included

The **seven stored procedures**. SQLite has none at all, and the other five spell them differently
enough that porting them would mean five versions of something no sample report calls.

---

## Loading a script

Each script creates the tables, the data and the views. **It does not create the database itself** -
make an empty one first, then run the script into it.

```sh
# SQLite - only if you want to rebuild it; SQLite/northwind.db is already here
sqlite3 my-northwind.db < SQLite/northwind.sql

# SQL Server - -f 65001 tells sqlcmd the file is UTF-8
sqlcmd -S localhost -U sa -P <password> -Q "CREATE DATABASE Northwind"
sqlcmd -S localhost -U sa -P <password> -d Northwind -f 65001 -i MSSQL/northwind.sql

# PostgreSQL
createdb -h localhost -U postgres northwind
psql -h localhost -U postgres -d northwind -f PostgreSQL/northwind.sql

# MySQL / MariaDB
mysql -h localhost -u root -p -e "CREATE DATABASE northwind CHARACTER SET utf8mb4"
mysql -h localhost -u root -p northwind < MySql/northwind.sql

# Oracle - runs into the schema you log in as
sqlplus <user>/<password>@localhost:1521/FREEPDB1 @Oracle/northwind.sql

# Firebird - create the database first; the script has no DROP statements
isql -q -u SYSDBA -p <password> -i Firebird/northwind.sql <path-to-new-database.fdb>
```

The first four scripts start by dropping any earlier copy, so they can be run again. Oracle and
Firebird have no `DROP ... IF EXISTS`, so run those against an **empty** database.

**The scripts are UTF-8, and the accented names depend on the client reading them that way.** The
MySQL and PostgreSQL scripts say so themselves (`SET NAMES utf8mb4`, `SET client_encoding`); `sqlcmd`
needs the `-f 65001` above. If `Côte de Blaye` comes out garbled, or as `C?te de Blaye`, the
file was read in the wrong encoding - drop the database and load it again.

The Oracle script starts with `SET DEFINE OFF`. Without it SQL\*Plus treats the `&` in
`Heli Süßwaren GmbH & Co. KG` as a variable to prompt for, and skips the row.

---

## Column types

SQL Server's types, mapped to the nearest thing each product has:

| Microsoft | SQLite | PostgreSQL | MySQL | Oracle | Firebird |
|---|---|---|---|---|---|
| `int` | `INTEGER` | `INTEGER` | `INT` | `NUMBER(10)` | `INTEGER` |
| `smallint` | `INTEGER` | `SMALLINT` | `SMALLINT` | `NUMBER(5)` | `SMALLINT` |
| `bit` | `INTEGER` | `SMALLINT` | `SMALLINT` | `NUMBER(1)` | `SMALLINT` |
| `nvarchar(n)` | `TEXT` | `VARCHAR(n)` | `VARCHAR(n)` | `NVARCHAR2(n)` | `VARCHAR(n)` |
| `nchar(n)` | `TEXT` | `CHAR(n)` | `CHAR(n)` | `NCHAR(n)` | `CHAR(n)` |
| `ntext` | `TEXT` | `TEXT` | `LONGTEXT` | `NCLOB` | `BLOB SUB_TYPE TEXT` |
| `money` | `NUMERIC` | `NUMERIC(19,4)` | `DECIMAL(19,4)` | `NUMBER(19,4)` | `DECIMAL(19,4)` |
| `real` | `REAL` | `NUMERIC(5,4)` | `DECIMAL(5,4)` | `NUMBER(5,4)` | `DECIMAL(5,4)` |
| `datetime` | `DATETIME` | `TIMESTAMP` | `DATETIME` | `DATE` | `TIMESTAMP` |
| `image` | `BLOB` | `BYTEA` | `LONGBLOB` | `BLOB` | `BLOB SUB_TYPE BINARY` |

**`real` is the one type that is not copied.** It is used by a single column,
`Order Details.Discount`, and the values in it are percentages such as `0.15`. Stored as a real,
`0.15` becomes `0.150000006...`, and each product then works out
`UnitPrice * Quantity * (1 - Discount)` at its own precision - so the same sales report came out a
cent apart from one product to the next, on SQL Server as well. Every copy here, SQL Server
included, stores the value that was meant, as an exact `DECIMAL(5,4)`. SQLite has no exact decimal
type, so it keeps `REAL`, but with `0.15` itself rather than the rounding error.

Checked against the exact sum of every order and every product: all six print the same totals, to
the cent.

**`nchar` is fixed width, and the padding is real data.** `Region.RegionDescription` is `nchar(50)`,
so `Eastern` comes back as `Eastern` followed by 43 spaces - on SQL Server and on every copy here.
That is how Microsoft's schema behaves; trim it in your query if you do not want it.

---

## Three things to know before you write SQL against these

### `Order Details` has a space in it, and nothing else does

It keeps its original name, so it has to be quoted - and each product quotes differently:

```sql
"Order Details"     -- SQLite, PostgreSQL, Oracle, Firebird
[Order Details]     -- SQL Server
`Order Details`     -- MySQL
```

Every other table and column is written **without** quotes, on purpose. PostgreSQL folds an unquoted
name to lower case, Oracle and Firebird fold to upper case, and they do it to your query as well as
to the script - so `SELECT ... FROM Customers` finds the table on all six. Most of the view names
have spaces too, so those need quoting as well.

MySQL reads `"Order Details"` as a *string*, not a name, unless the server runs with `ANSI_QUOTES`.

### SQLite compares dates as text

SQLite has no date type. A `datetime` is stored as `1997-12-31 00:00:00`, and comparisons are string
comparisons - so `BETWEEN '1997-01-01' AND '1997-12-31'` **excludes** everything shipped on the last
day, because `'1997-12-31 00:00:00'` sorts after `'1997-12-31'`. The views here write the full form
on both ends so they mean the same thing they mean on SQL Server. Do the same in your own queries.

### The same `ORDER BY` gives a different order on different servers

Sorting is done by the server, using the collation of the database you loaded into - not by
BluePrint. Northwind is full of accented letters, and a server decides for itself whether an accented
letter sorts next to its plain form or after `z`. Both answers are correct; the rows and the totals
are identical either way, only the order changes. If a report has to come out the same everywhere,
sort it in the report rather than in the query.

---

## Coming from Microsoft's Northwind for SQL Server?

Nothing is renamed and no rows are added or removed - `Region` is `Region`, `Customers` has 91 rows.
The only differences are the seven stored procedures, which are left out, the type mapping above,
`Discount`, which is an exact decimal instead of a `real` (see **Column types**), and the pictures,
which are plain bitmaps instead of OLE objects (see **Images**).

---

## Pointing BluePrint at one of these

Add a database profile in Studio (**Database > Database Profile...**) and pick the product. The
driver comes from the `DataProvider` folder next to `BluePrint.Studio.exe` - see **Connecting to a
Database** in the manual, and [`../NorthwindBpt/README.md`](../NorthwindBpt/README.md) for the SQLite
walkthrough.

**All thirteen sample reports in [`../NorthwindBpt/`](../NorthwindBpt/) run on all six, exactly as
they are.** Point a report at any of these databases and press Retrieve - there is nothing to edit.
Each one was run against a real server of every product and printed the same rows and the same text.

That is not because the six agree with each other; they do not. The reports are written with
**portable functions** (`{bp:...}`), which BluePrint turns into whatever the database in use
expects:

| Where the databases differ | Written as | Becomes, for example |
|---|---|---|
| Joining text - `\|\|`, `+`, `CONCAT` | `{bp:Concat(a, ', ', b)}` | `a + ', ' + b` on SQL Server |
| A date as `1996-07-04` text | `{bp:DateText(o.OrderDate)}` | `DATE_FORMAT(o.OrderDate, '%Y-%m-%d')` on MySQL |
| `Order Details` - the one name with a space | `{bp:Name(Order Details)}` | `` `Order Details` `` on MySQL, `[Order Details]` on SQL Server |
| The first ten rows | `{bp:Limit(10)}` | `FETCH FIRST 10 ROWS ONLY` on Oracle |
| The year of a date | `{bp:Year(d)}` | `EXTRACT(YEAR FROM d)` on Firebird |

**Keep the dates as text.** The reports declare `OrderDate` as text and print it as it comes, so the
query produces `1996-07-04` on every product. Return a real date instead and it is printed in the
format of whatever machine runs the report - `7/4/1996 12:00:00 AM` on one, `04/07/1996 00:00:00`
on another.

**Writing your own query?** `CAST(x AS REAL)` on the money columns is only needed on SQLite (see
the note in [`../NorthwindBpt/README.md`](../NorthwindBpt/README.md)); it is harmless on the others.
A quarter written as `(month + 2) / 3` is **not** portable - MySQL answers `3.0000` where the others
answer `3` - so report 08 spells it out with `CASE WHEN`. And `GROUP BY` an alias from the select
list is refused by SQL Server and Firebird: repeat the expression instead.