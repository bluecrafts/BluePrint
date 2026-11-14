# Showcase

Complete applications built on BluePrint, larger than the samples in
[`Demo`](../Demo): each one solves a whole task from start to finish, the way
an application of your own would.

| Application | Shows |
| --- | --- |
| [`LabelPrinting`](LabelPrinting) | design a label with `BluePrintDesignerControl`, fill it from a data file, and print it straight to a label printer as a 1-bit image in the printer's own command language, with no printer driver in between |
| [`Localization`](Localization) | one invoice report in seven languages: pick a form, an order of the Northwind database and a language, and see the texts, the dates and numbers and the fonts change - with the translations kept in seven different ways, from inside the report to a translator's CSV file, and a panel that tells where each text comes from |
| [`ReceiptPrinting`](ReceiptPrinting) | open a receipt form for 80 mm or 58 mm paper, fill it from the Northwind database with retrieval arguments, see the 1-bit image the printer will get, and print it straight to a receipt printer in ESC/POS - receipts from a few lines to 5,000 |
| [`SharingHTMLReportsViaEmail`](SharingHTMLReportsViaEmail) | pick one of seven monthly reports of the Northwind database - tables, charts and a crosstab - and a month, and send it as HTML in the body of an email - with inline styles that Gmail and Outlook keep, charts as pictures inside the message, the size checked against the 102 KB Gmail shows, and SMTP, `.eml` or your own mail program to send it |
| [`ReportServer`](ReportServer) | a report server and the web page that shows its reports: pick one of six Northwind reports - invoices with a barcode and a QR code among them - and its conditions, turn the pages one at a time - each page comes from the server on its own, as HTML - restyle it with five CSS themes, print the page on the screen or download a PDF. An ASP.NET Core API and a web site, on Windows, on Linux or in Docker |

Like the samples, each application references the BlueCrafts packages from
nuget.org by an exact version, so what you build here is what the published
packages give you.

The first four are WPF applications and need Windows:

```
dotnet run --project LabelPrinting
dotnet run --project Localization
dotnet run --project ReceiptPrinting
dotnet run --project SharingHTMLReportsViaEmail
```

ReportServer runs anywhere .NET runs - two programs, the API and the web:

```
dotnet run --project ReportServer/ReportServerAPI
dotnet run --project ReportServer/ReportServerWeb
```

or both in containers, with `docker compose up --build` in its folder.

Each application has a README of its own that walks through it.
