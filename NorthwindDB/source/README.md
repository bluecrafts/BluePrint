# Where the data comes from

`instnwnd.sql` is Microsoft's own Northwind install script, taken unchanged from
[microsoft/sql-server-samples](https://github.com/microsoft/sql-server-samples/tree/master/samples/databases/northwind-pubs).

**It is the source of truth for everything in this folder.** The six `northwind.sql` scripts, the
SQLite `northwind.db` and the Firebird `northwind.fdb` are all generated from it, which is why they
all hold exactly the same rows. Nothing here is edited by hand.

It is kept in the repository so that anyone can check that claim for themselves rather than take it
on trust.

| | |
|---|---|
| Declares | 13 tables, 16 views, 7 stored procedures |
| Rows | 3,308 `INSERT` statements |
| Size | about 1 MB, of which roughly 570 KB is the `Picture` and `Photo` image data in hex |

## What is used and what is not

The generator reads the **tables, the rows and the views**. The **seven stored procedures are
skipped**: SQLite has no stored procedures at all, and the other five products each spell them
differently enough that porting them would mean writing five versions of something no sample report
calls.

The generator changes two things:

- **The 17 pictures** - each is an **OLE object** (a 78-byte header, then the bitmap), and
  only the bitmap is kept, so that an ordinary image viewer can open it.
- **`Order Details.Discount`** - a `real` here, so `0.15` is stored as `0.150000006...`. The generator
  writes the value that was meant (`0.15`) into an exact `DECIMAL(5,4)`, so that every product
  computes the same totals.

## Loading it into SQL Server

The generator reads this script back out of a SQL Server database, and **that database must use a
Latin collation** such as `Latin1_General_CI_AS`:

```sh
sqlcmd -S <host> -U sa -P <password> -Q "CREATE DATABASE Northwind COLLATE Latin1_General_CI_AS"
sqlcmd -S <host> -U sa -P <password> -d Northwind -f 65001 -i instnwnd.sql
```

Most of the names are written as plain `'...'` literals, not `N'...'`, so SQL Server converts them
through the code page of the database's collation. A collation whose code page lacks `ö`, `é` or `ñ` -
`Thai_CI_AS`, for one - stores every such letter as `?`, silently.

## Licence

Microsoft SQL Server Sample Code, Copyright (c) Microsoft Corporation, released under the MIT
Licence. The full text is in
[`license.txt`](https://github.com/microsoft/sql-server-samples/blob/master/license.txt) in that
repository:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction,
> including without limitation the rights to use, copy, modify, merge, publish, distribute,
> sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
> NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
> OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
