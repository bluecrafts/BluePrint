// The body of the email: the report exported to HTML with every style inline,
// with the message the user typed above it and a short footer below it.
//
// Mail programs are not browsers. Gmail and Outlook for Windows remove <style>
// blocks or parts of them, and they do not support position or flexbox at all. What
// every one of them shows is a table with the styles written on each cell - the
// "Styled table" layout. The other two layouts are here to compare:
//
//   Styled table  TableLayout + InlineCss   colours, fonts and lines on every cell
//   Plain table   TableLayout               no styles at all: small, but black and white
//   Absolute      Absolute + InlineCss      the same as the PDF in a browser, but
//                                           Gmail and Outlook for Windows lose the layout
//
// Charts: mail programs do not show an inline <svg>, and Gmail and Outlook do not
// show a picture written into the HTML (data: URI) either. What they show is a
// picture that travels inside the message and that the HTML names with "cid:" -
// so the charts are exported as PNG files and each becomes a part of the message
// (MailComposer). The Email tab and Open in Browser write the same files next to
// the page instead.
//
// The report is rendered once (RenderedDocument) and exported again for every
// change of the options: there is nothing to read from the database again.

using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using BluePrint.Render.Export;
using BluePrint.Render.Export.Html;
using BluePrint.Render.Models;

namespace SharingHtmlReports.Mail;

public enum EmailLayout
{
    /// <summary>A table with the styles of the report on every cell: shows the same in every mail program.</summary>
    StyledTable,

    /// <summary>A table with no styles: the smallest message, without colours or lines.</summary>
    PlainTable,

    /// <summary>Every text placed where it is on paper: like the PDF, but only in mail programs that support CSS positioning.</summary>
    Absolute,
}

public enum TablePages
{
    /// <summary>One table for the whole report: column headings and page numbers once.</summary>
    OnePage,

    /// <summary>One table per printed page, like the paper report.</summary>
    PerPage,
}

public enum PageFlow
{
    /// <summary>One long page: page headers and footers only at the top and at the end.</summary>
    SingleFile,

    /// <summary>The printed pages one under the other, each with its page header and footer.</summary>
    SingleFilePaged,
}

/// <summary>The HTML options of the email.</summary>
public sealed record EmailHtmlOptions(
    EmailLayout Layout = EmailLayout.StyledTable,
    TablePages TablePages = TablePages.OnePage,
    PageFlow PageFlow = PageFlow.SingleFilePaged)
{
    public bool IsTable => Layout != EmailLayout.Absolute;

    /// <summary>The options of the BluePrint HTML export that give this layout.</summary>
    public HtmlExportOptions ToExportOptions(string title) => new()
    {
        LayoutMode = IsTable ? HtmlLayoutMode.TableLayout : HtmlLayoutMode.Absolute,
        TablePageMode = TablePages == TablePages.OnePage ? HtmlTablePageMode.Infinity : HtmlTablePageMode.Wysiwyg,
        Mode = PageFlow == PageFlow.SingleFile ? HtmlExportMode.SingleFile : HtmlExportMode.SingleFilePaged,
        // Styles on the elements themselves: mail programs remove <style> blocks.
        InlineCss = Layout != EmailLayout.PlainTable,
        // Charts as pictures, in files of their own: they go into the message as cid: parts.
        ChartFormat = HtmlChartFormat.Png,
        EmbedImages = false,
        Title = title,
    };
}

/// <summary>A picture of the message (a chart), sent inside it and named in the HTML by <c>cid:ContentId</c>.</summary>
public sealed record EmailImage(string ContentId, string FileName, byte[] Content);

public enum EmailNoteKind { Information, Warning }

/// <summary>Something the user should know before sending.</summary>
public sealed record EmailNote(EmailNoteKind Kind, string Text);

/// <summary>The HTML of the message and what is known about it.</summary>
/// <param name="Html">The HTML as sent: pictures are named <c>cid:</c> and their Content-ID.</param>
/// <param name="Bytes">The size of the HTML alone - what Gmail counts for its 102 KB.</param>
public sealed record EmailBody(string Html, int Bytes, int Tables, int Pages, IReadOnlyList<EmailNote> Notes,
                               IReadOnlyList<EmailImage> Images)
{
    public string SizeText => HtmlEmailBuilder.FormatSize(Bytes);

    public string SummaryText =>
        $"{SizeText} - {(Tables == 0 ? "absolute layout" : Tables == 1 ? "1 table" : $"{Tables} tables")} - " +
        (Pages == 1 ? "1 page" : $"{Pages} pages") +
        (Images.Count == 0 ? "" : Images.Count == 1 ? " - 1 chart" : $" - {Images.Count} charts");

    /// <summary>
    /// The HTML for a web browser: every <c>cid:</c> picture becomes <c>folder/file</c> - with
    /// <see cref="WriteImages"/> into that folder next to the page.
    /// </summary>
    public string HtmlWithFolder(string folder) =>
        Images.Aggregate(Html, (html, image) =>
            html.Replace($"src=\"cid:{image.ContentId}\"", $"src=\"{folder}/{image.FileName}\"", StringComparison.Ordinal));

    /// <summary>Writes the pictures into <paramref name="directory"/> (created when missing).</summary>
    public void WriteImages(string directory)
    {
        if (Images.Count == 0) return;
        Directory.CreateDirectory(directory);
        foreach (var image in Images) File.WriteAllBytes(Path.Combine(directory, image.FileName), image.Content);
    }
}

public static class HtmlEmailBuilder
{
    /// <summary>Gmail shows the first 102 KB of a message and hides the rest behind "View entire message".</summary>
    public const int GmailClipBytes = 102 * 1024;

    /// <summary>Above this, many mail servers refuse the message.</summary>
    public const int LargeMessageBytes = 1024 * 1024;

    private const string FontStack = "'Segoe UI',Arial,sans-serif";

    /// <param name="report">The rendered report.</param>
    /// <param name="title">The title of the HTML document: the subject of the message.</param>
    /// <param name="message">The text typed by the user, shown above the report. Plain text.</param>
    /// <param name="footer">One line below the report. Plain text.</param>
    /// <param name="rows">How many rows the report read: none gives a note.</param>
    public static EmailBody Build(RenderedDocument report, EmailHtmlOptions options, string title,
                                  string message, string footer, int rows)
    {
        var (html, images) = ExportWithImages(report, options.ToExportOptions(title));
        html = Insert(html, MessageHtml(message), FooterHtml(footer));

        int bytes = Encoding.UTF8.GetByteCount(html);
        int tables = options.IsTable ? CountTables(html) : 0;
        // the whole message: the pictures travel in base64, a third bigger than the files
        long total = bytes + images.Sum(i => (long)i.Content.Length) * 4 / 3;
        return new EmailBody(html, bytes, tables, report.Pages.Count, Notes(options, bytes, rows, total), images);
    }

    /// <summary>
    /// The report as a file to attach: the absolute layout with its CSS classes, as the PDF -
    /// for opening in a web browser when the mail program shows the body differently. Charts
    /// stay vector (SVG): a browser shows them.
    /// </summary>
    public static string BuildAttachment(RenderedDocument report, string title) =>
        ExportWithImages(report, new HtmlExportOptions { Mode = HtmlExportMode.SingleFilePaged, Title = title }).Html;

    public static IReadOnlyList<EmailNote> Notes(EmailHtmlOptions options, int bytes, int rows, long totalBytes = 0)
    {
        totalBytes = Math.Max(totalBytes, bytes);
        var notes = new List<EmailNote>();
        if (rows == 0)
            notes.Add(new(EmailNoteKind.Information, "The report has no rows for this month. The message will say so."));
        if (options.Layout == EmailLayout.Absolute)
            notes.Add(new(EmailNoteKind.Warning,
                "Gmail and Outlook for Windows do not support absolute positioning: the report loses its layout there. " +
                "Use Styled table for email."));
        if (totalBytes > LargeMessageBytes)
            notes.Add(new(EmailNoteKind.Warning,
                $"The message is {FormatSize(totalBytes)}. Many mail servers refuse large messages: " +
                "use Plain table, or attach the report as a file."));
        else if (bytes > GmailClipBytes)
            notes.Add(new(EmailNoteKind.Warning,
                $"The message body is {FormatSize(bytes)}. Gmail shows only the first 102 KB and hides the rest " +
                "behind \"View entire message\". Plain table is smaller."));
        return notes;
    }

    public static string FormatSize(long bytes) => bytes < 1024 * 1024
        ? (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB"
        : (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    // ---- the parts the application adds ---------------------------------------------

    /// <summary>
    /// The user's message as HTML: escaped, one &lt;br&gt; per line break, on a white
    /// block of its own (the absolute layout gives the page a grey background).
    /// </summary>
    public static string MessageHtml(string message)
    {
        var text = (message ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (text.Length == 0) return "";
        var lines = text.Split('\n').Select(WebUtility.HtmlEncode);
        return "<div style=\"background:#FFFFFF;color:#1F2937;font-family:" + FontStack +
               ";font-size:11pt;line-height:1.5;padding:12px 16px;margin:0 0 12px 0\">" +
               string.Join("<br>\n", lines) + "</div>\n";
    }

    public static string FooterHtml(string footer) => string.IsNullOrWhiteSpace(footer)
        ? ""
        : "<div style=\"background:#FFFFFF;color:#6B7280;font-family:" + FontStack +
          ";font-size:8.5pt;padding:12px 16px;margin:12px 0 0 0;border-top:1px solid #E3E7EC\">" +
          WebUtility.HtmlEncode(footer) + "</div>\n";

    /// <summary>
    /// Puts <paramref name="top"/> right after the opening &lt;body&gt; tag and <paramref name="bottom"/>
    /// right before &lt;/body&gt;. The HTML comes from BluePrint, whose shape is known: there is no
    /// need to parse it.
    /// </summary>
    public static string Insert(string html, string top, string bottom)
    {
        int body = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        int open = body < 0 ? -1 : html.IndexOf('>', body);
        int close = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (open < 0 || close < open) throw new FormatException("The exported HTML has no <body> element.");

        return new StringBuilder(html.Length + top.Length + bottom.Length)
            .Append(html, 0, open + 1).Append('\n').Append(top)
            .Append(html, open + 1, close - open - 1)
            .Append(bottom)
            .Append(html, close, html.Length - close)
            .ToString();
    }

    private const string PageName = "report.html";

    /// <summary>
    /// The HTML and its pictures. With <c>EmbedImages = false</c> BluePrint writes each picture as a
    /// file of its own ("report_files/img1.png") before the page; here each one gets a Content-ID and
    /// the HTML names it <c>cid:</c>.
    /// </summary>
    private static (string Html, IReadOnlyList<EmailImage> Images) ExportWithImages(RenderedDocument report, HtmlExportOptions options)
    {
        var sink = new MemoryExportSink();
        HtmlRenderer.ExportTo(sink, report, PageName, options);
        var html = Encoding.UTF8.GetString(sink.Parts.Single(p => p.Name == PageName).Content);

        var images = new List<EmailImage>();
        foreach (var part in sink.Parts.Where(p => p.Name != PageName))
        {
            string src = part.Name.Replace('\\', '/');
            string file = Path.GetFileName(src);
            // unique in the message, and the same for the same report: tests and .eml files compare
            var image = new EmailImage($"{Path.GetFileNameWithoutExtension(file)}@report", file, part.Content);
            html = html.Replace($"src=\"{src}\"", $"src=\"cid:{image.ContentId}\"", StringComparison.Ordinal);
            images.Add(image);
        }
        return (html, images);
    }

    private static int CountTables(string html)
    {
        int count = 0, at = 0;
        while ((at = html.IndexOf("<table", at, StringComparison.Ordinal)) >= 0) { count++; at += 6; }
        return count;
    }
}
