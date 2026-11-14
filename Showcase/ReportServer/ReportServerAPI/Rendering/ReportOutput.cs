// The two outputs of a rendered report: the HTML of one page, and the PDF.
//
// Both start from the RenderedDocument in memory - nothing is rendered again, and
// nothing is written to disk: the HTML goes through a MemoryExportSink and the PDF
// through a MemoryStream.

using System.Globalization;
using BluePrint.Render.Export;
using BluePrint.Render.Export.Html;
using BluePrint.Render.Export.Pdf;
using BluePrint.Render.Models;
using BluePrint.Render.Printing;

namespace ReportServerApi.Rendering;

/// <summary>How the HTML of a page lays the report out.</summary>
public enum PageLayout
{
    /// <summary>
    /// Every element where the report puts it (position:absolute), with its own fonts,
    /// colours and lines: the page looks like the PDF. A theme changes what is around
    /// the page - the background, the shadow, a colour filter - not the report.
    /// </summary>
    Paper,

    /// <summary>
    /// A plain &lt;table&gt; with no style of BluePrint at all: the theme's CSS decides
    /// how it looks. Lines, boxes and charts are left out.
    /// </summary>
    Table,
}

public static class ReportOutput
{
    public static byte[] PageHtml(RenderedDocument document, int page, PageLayout layout)
    {
        var options = new HtmlExportOptions
        {
            // One page per request: the page range picks it out of the rendered report.
            PageRange = BluePrintPageRange.Parse(page.ToString(CultureInfo.InvariantCulture)),
            Mode = HtmlExportMode.SingleFilePaged,
            LayoutMode = layout == PageLayout.Table ? HtmlLayoutMode.TableLayout : HtmlLayoutMode.Absolute,
            TablePageMode = HtmlTablePageMode.Wysiwyg,
            // Class-based CSS (the default): a theme can override it, and the page is
            // smaller than with every style repeated inline.
            InlineCss = false,
            // Pictures and charts inside the page itself (data: URI and inline SVG), so
            // one request brings the whole page.
            EmbedImages = true,
            Title = $"{document.Definition.Metadata.Name} - page {page}",
        };

        var sink = new MemoryExportSink();
        HtmlRenderer.ExportTo(sink, document, "page.html", options);
        return sink.Parts[0].Content;
    }

    /// <param name="pages">null = every page; otherwise a page range such as "3" or "1-5".</param>
    public static byte[] Pdf(RenderedDocument document, string? pages)
    {
        var selected = pages is null ? document : PageSelector.Apply(document, BluePrintPageRange.Parse(pages));
        using var stream = new MemoryStream();
        BluePrintPdfRenderer.Export(selected, stream);
        return stream.ToArray();
    }
}
