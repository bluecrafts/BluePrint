// One HTML page holding every symbol the run produced.
//
// The SVG goes straight into the page. Nothing is linked and nothing is
// encoded into a data URI: BarcodeSvgWriter returns markup, and markup is what
// a browser wants, so the whole sheet is one file that opens anywhere.
//
// This is also the honest way to look at a barcode before printing it. Zoom in
// as far as you like and the bar edges stay exactly where the encoder put them.

using System.Text;
using BluePrint.Barcode;
using Demo.Shared;

namespace Demo.Barcode;

/// <summary>One symbol that encoded and drew, ready to be shown on the sheet.</summary>
public sealed record SheetEntry(BarcodeSample Sample, BarcodeSymbol Symbol, string Svg, bool ShowHri);

public static class ContactSheet
{
    public static string Build(IReadOnlyList<SheetEntry> entries)
    {
        var html = new StringBuilder(64 * 1024);

        html.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>BluePrint.Barcode</title>
            <style>
              :root {
                --bg:      #f6f7f9;
                --card:    #ffffff;
                --ink:     #16191d;
                --muted:   #5d6672;
                --line:    #dfe3e8;
                --accent:  #1f6feb;
              }
              @media (prefers-color-scheme: dark) {
                :root {
                  --bg:    #0f1216;
                  --card:  #171b21;
                  --ink:   #e6e9ee;
                  --muted: #9aa4b2;
                  --line:  #262c35;
                  --accent:#4d9dff;
                }
              }
              * { box-sizing: border-box; }
              body {
                margin: 0; padding: 40px 24px 64px;
                background: var(--bg); color: var(--ink);
                font: 15px/1.55 "Segoe UI", system-ui, -apple-system, sans-serif;
              }
              .page { max-width: 1120px; margin: 0 auto; }
              h1 { font-size: 26px; margin: 0 0 6px; letter-spacing: -0.01em; }
              .lede { color: var(--muted); margin: 0 0 36px; max-width: 62ch; }
              h2 {
                font-size: 13px; text-transform: uppercase; letter-spacing: 0.09em;
                color: var(--accent); margin: 40px 0 14px;
                padding-bottom: 8px; border-bottom: 1px solid var(--line);
              }
              .grid {
                display: grid; gap: 18px;
                grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
              }
              .card {
                background: var(--card); border: 1px solid var(--line);
                border-radius: 10px; padding: 18px 18px 16px;
                display: flex; flex-direction: column;
              }
              .title { font-weight: 600; margin-bottom: 2px; }
              .sub { color: var(--muted); font-size: 12.5px; margin-bottom: 14px; }
              .symbol {
                background: #ffffff; border: 1px solid var(--line); border-radius: 6px;
                padding: 14px; text-align: center; margin-bottom: 12px;
              }
              .symbol svg { max-width: 100%; height: auto; }
              .hri {
                font-family: Consolas, "SF Mono", monospace; font-size: 13px;
                color: #16191d; margin-top: 8px; letter-spacing: 0.06em;
              }
              .note { color: var(--muted); font-size: 13px; margin: 0 0 12px; flex: 1; }
              .data {
                font-family: Consolas, "SF Mono", monospace; font-size: 12.5px;
                background: var(--bg); border: 1px solid var(--line); border-radius: 6px;
                padding: 8px 10px; word-break: break-all;
              }
              footer { color: var(--muted); font-size: 13px; margin-top: 48px; }
            </style>
            </head>
            <body>
            <div class="page">
            <h1>BluePrint.Barcode</h1>
            <p class="lede">Every symbology the library encodes, drawn straight from the module
            grid. Each symbol below is inline SVG, so it stays sharp at any zoom and no image
            file is involved.</p>

            """);

        foreach (var family in entries.Select(e => e.Sample.Family).Distinct())
        {
            html.Append("<h2>").Append(Escape(family)).Append("</h2>\n<div class=\"grid\">\n");

            foreach (var entry in entries.Where(e => e.Sample.Family == family))
            {
                var sample = entry.Sample;
                var symbol = entry.Symbol;

                html.Append("<div class=\"card\">\n")
                    .Append("<div class=\"title\">").Append(Escape(sample.Title)).Append("</div>\n")
                    .Append("<div class=\"sub\">").Append(symbol.Kind == SymbolKind.Matrix2D ? "2D" : "linear")
                    .Append(" &middot; ").Append(symbol.Rows).Append(" x ").Append(symbol.Columns)
                    .Append(" modules &middot; quiet zone ").Append(symbol.QuietZoneLeft)
                    .Append('/').Append(symbol.QuietZoneRight).Append("</div>\n")
                    .Append("<div class=\"symbol\">").Append(entry.Svg);

                if (entry.ShowHri && !string.IsNullOrEmpty(symbol.HriText))
                    html.Append("<div class=\"hri\">").Append(Escape(symbol.HriText)).Append("</div>");

                html.Append("</div>\n")
                    .Append("<p class=\"note\">").Append(Escape(sample.Note)).Append("</p>\n")
                    .Append("<div class=\"data\">").Append(Escape(sample.Data)).Append("</div>\n")
                    .Append("</div>\n");
            }

            html.Append("</div>\n");
        }

        html.Append("""
            <footer>Generated by Demo.Barcode. The text under a symbol is what the encoder
            says should be printed there, which is not always what was typed in: a check digit
            it worked out, or the brackets a GS1 value needs for a reader and not for a
            scanner.</footer>
            </div>
            </body>
            </html>
            """);

        return html.ToString();
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
