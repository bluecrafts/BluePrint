// Three ways out for a message:
//
//   Send              through an SMTP server (System.Net.Mail.SmtpClient)
//   Save .eml         a file any mail program opens
//   Open in Mail App  the same file with "X-Unsent: 1", opened by Windows: Outlook
//                     shows it as a new message ready to send
//
// The .eml file is written by SmtpClient itself, into a pickup folder: .NET has no
// other public way to turn a MailMessage into MIME.
//
// SmtpClient uses STARTTLS (port 587) when EnableSsl is set. It does not support
// TLS from the first byte (port 465), and it does not support OAuth2: with Gmail or
// Microsoft 365, use an app password or a relay of your organization.

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mail;

namespace SharingHtmlReports.Mail;

public enum SmtpSecurity
{
    /// <summary>Port 587: the connection starts in plain text and switches to TLS.</summary>
    StartTls,

    /// <summary>No encryption: a relay on your own network, or a test server on this computer.</summary>
    None,
}

public sealed class SmtpSettings
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public SmtpSecurity Security { get; init; } = SmtpSecurity.StartTls;
    public string User { get; init; } = "";

    /// <summary>In plain text, as the settings file keeps it.</summary>
    public string Password { get; init; } = "";
}

public static class MailSender
{
    public const int ImplicitTlsPort = 465;

    /// <summary>Why these settings cannot work, or null.</summary>
    public static string? Problem(SmtpSettings smtp)
    {
        if (string.IsNullOrWhiteSpace(smtp.Host)) return "Enter the SMTP server.";
        if (smtp.Port is < 1 or > 65535) return "The port must be a number from 1 to 65535.";
        if (smtp.Port == ImplicitTlsPort)
            return "Port 465 needs implicit TLS, which this application does not support. Use port 587 with STARTTLS.";
        return null;
    }

    /// <exception cref="InvalidOperationException">The settings cannot work (see <see cref="Problem"/>).</exception>
    /// <exception cref="SmtpException">The server refused the message or could not be reached.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static async Task SendAsync(MailMessage message, SmtpSettings smtp, CancellationToken ct = default)
    {
        if (Problem(smtp) is { } problem) throw new InvalidOperationException(problem);

        using var client = new SmtpClient(smtp.Host.Trim(), smtp.Port)
        {
            EnableSsl = smtp.Security == SmtpSecurity.StartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 100_000,
        };
        if (!string.IsNullOrEmpty(smtp.User))
            client.Credentials = new NetworkCredential(smtp.User, smtp.Password);
        await client.SendMailAsync(message, ct).ConfigureAwait(false);
    }

    /// <summary>Writes the message to <paramref name="path"/> as a .eml file.</summary>
    /// <param name="unsent">Adds "X-Unsent: 1": Outlook then opens the file as a new message to send.</param>
    public static void SaveEml(MailMessage message, string path, bool unsent = false)
    {
        var pickup = Directory.CreateTempSubdirectory("bp-eml-");
        if (unsent) message.Headers["X-Unsent"] = "1";
        try
        {
            using (var client = new SmtpClient
                   {
                       DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                       PickupDirectoryLocation = pickup.FullName,
                   })
            {
                client.Send(message);
            }
            var written = Directory.GetFiles(pickup.FullName, "*.eml").Single();
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.Move(written, path, overwrite: true);
        }
        finally
        {
            if (unsent) message.Headers.Remove("X-Unsent");
            try { pickup.Delete(recursive: true); } catch (IOException) { /* a temporary folder left behind */ }
        }
    }

    /// <summary>Saves the message as an unsent .eml in the temporary folder and opens it with the program Windows uses for .eml files.</summary>
    /// <returns>The file that was opened.</returns>
    public static string OpenInMailApp(MailMessage message, string fileName)
    {
        var folder = Path.Combine(Path.GetTempPath(), "SharingHTMLReportsViaEmail");
        var path = Path.Combine(folder, fileName + ".eml");
        SaveEml(message, path, unsent: true);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return path;
    }
}
