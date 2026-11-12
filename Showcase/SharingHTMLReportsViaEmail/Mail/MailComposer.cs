// The email itself, as a System.Net.Mail.MailMessage:
//
//   multipart/mixed
//     multipart/alternative
//       text/plain   the message and one line about the report, for programs that show text only
//       multipart/related
//         text/html  the body from HtmlEmailBuilder
//         image/png  one per chart, named in the HTML by cid: (only when the report has charts)
//     text/html      the report as a file (when "Attach as .html file" is on)
//
// Everything is UTF-8. The HTML is sent in base64: its lines are very long, and
// quoted-printable would make it bigger.

using System.IO;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace SharingHtmlReports.Mail;

/// <summary>What goes into the message.</summary>
public sealed class MailInput
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Cc { get; init; } = "";
    public required string Subject { get; init; }

    /// <summary>The text the user typed: the start of the plain-text part.</summary>
    public string Message { get; init; } = "";

    public required EmailBody Body { get; init; }

    /// <summary>The name of the report and the month, for the plain-text part.</summary>
    public required string ReportTitle { get; init; }

    /// <summary>The report as an HTML file to attach, or null.</summary>
    public string? AttachmentHtml { get; init; }

    public string AttachmentName { get; init; } = "report.html";
}

public static class MailComposer
{
    public const string DefaultSubject = "{Report} - {Month}";

    /// <summary>Used as the sender of a saved .eml when no sender is given: a .eml needs one.</summary>
    public const string PlaceholderFrom = "reports@example.com";

    /// <summary>"{Report} - {Month}" with the name of the report and the month.</summary>
    public static string ExpandSubject(string template, string report, string month)
    {
        var text = string.IsNullOrWhiteSpace(template) ? DefaultSubject : template;
        return text.Replace("{Report}", report, StringComparison.OrdinalIgnoreCase)
                   .Replace("{Month}", month, StringComparison.OrdinalIgnoreCase)
                   .Trim();
    }

    /// <summary>
    /// The other way round: a subject the user typed, with the name of the report and the month
    /// put back as {Report} and {Month} - so the next report or month takes their place.
    /// "Sales: Monthly Sales Journal for April 1998" becomes "Sales: {Report} for {Month}".
    /// </summary>
    public static string ToTemplate(string subject, string report, string month)
    {
        var text = subject ?? "";
        if (report.Length > 0) text = text.Replace(report, "{Report}", StringComparison.OrdinalIgnoreCase);
        if (month.Length > 0) text = text.Replace(month, "{Month}", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>
    /// The addresses in <paramref name="text"/>, separated by commas or semicolons.
    /// Returns false, with the reason, when one of them is not an email address.
    /// </summary>
    public static bool TryParseAddresses(string? text, out List<MailAddress> addresses, out string? error)
    {
        addresses = [];
        error = null;
        foreach (var part in (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!MailAddress.TryCreate(part, out var address) || !address.Host.Contains('.'))
            {
                error = $"\"{part}\" is not an email address.";
                return false;
            }
            addresses.Add(address);
        }
        return true;
    }

    /// <summary>The text/plain part: the message, then a line that says where the report is.</summary>
    public static string PlainText(MailInput input)
    {
        var text = new StringBuilder();
        var message = input.Message.Replace("\r\n", "\n").TrimEnd('\n');
        if (message.Length > 0) text.Append(message.Replace("\n", "\r\n")).Append("\r\n\r\n");
        text.Append($"This message contains the report {input.ReportTitle} in HTML.");
        if (input.AttachmentHtml is not null)
            text.Append($" If your mail program shows text only, open the attached file {input.AttachmentName} in a web browser.");
        return text.Append("\r\n").ToString();
    }

    /// <exception cref="FormatException">An address is not valid, or there is no recipient.</exception>
    public static MailMessage Compose(MailInput input)
    {
        if (!TryParseAddresses(input.From, out var from, out var error)) throw new FormatException("From: " + error);
        if (from.Count > 1) throw new FormatException("From: give one sender.");
        if (!TryParseAddresses(input.To, out var to, out error)) throw new FormatException("To: " + error);
        if (!TryParseAddresses(input.Cc, out var cc, out error)) throw new FormatException("Cc: " + error);
        if (to.Count == 0) throw new FormatException("To: give at least one recipient.");

        var message = new MailMessage
        {
            From = from.Count == 1 ? from[0] : new MailAddress(PlaceholderFrom),
            Subject = input.Subject,
            SubjectEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
        };
        foreach (var a in to) message.To.Add(a);
        foreach (var a in cc) message.CC.Add(a);

        var plain = AlternateView.CreateAlternateViewFromString(PlainText(input), Encoding.UTF8, MediaTypeNames.Text.Plain);
        plain.TransferEncoding = TransferEncoding.QuotedPrintable;
        var html = AlternateView.CreateAlternateViewFromString(input.Body.Html, Encoding.UTF8, MediaTypeNames.Text.Html);
        html.TransferEncoding = TransferEncoding.Base64;
        // the charts: parts of the HTML view (multipart/related), named in the HTML by cid:
        foreach (var image in input.Body.Images)
            html.LinkedResources.Add(new LinkedResource(new MemoryStream(image.Content), MediaTypeNames.Image.Png)
            {
                ContentId = image.ContentId,
                TransferEncoding = TransferEncoding.Base64,
                ContentType = { Name = image.FileName },
            });
        message.AlternateViews.Add(plain);   // the last alternative is the preferred one: HTML
        message.AlternateViews.Add(html);

        if (input.AttachmentHtml is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(input.AttachmentHtml);
            var attachment = new Attachment(new MemoryStream(bytes), input.AttachmentName, MediaTypeNames.Text.Html)
            {
                TransferEncoding = TransferEncoding.Base64,
            };
            attachment.ContentType.CharSet = "utf-8";
            message.Attachments.Add(attachment);
        }
        return message;
    }

    /// <summary>A file name made from the report and the month: "Monthly Sales Journal - 1998-04".</summary>
    public static string FileNameFor(string report, string monthKey)
    {
        var name = $"{report} - {monthKey}";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
