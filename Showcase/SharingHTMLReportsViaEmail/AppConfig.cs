// SharingHTMLReportsViaEmail.cfg: what the window remembers between runs, in one
// JSON file next to the executable - the report and month chosen last, the email
// (sender, recipients, subject, message), the HTML options, the SMTP server, the
// theme and the size of the window.
//
// The SMTP password is kept here too, in plain text, so the file can be copied to
// another computer with everything in it. Anyone who can open the folder can read it.
//
// Nothing in it is precious: a file that cannot be read gives the defaults. It is
// written to a .tmp file first and then moved over the old one, so a crash halfway
// through leaves the old file whole.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharingHtmlReports.Mail;

namespace SharingHtmlReports;

public sealed class AppConfig
{
    /// <summary>File name of the report chosen last, in the Samples folder.</summary>
    public string? LastReport { get; set; }

    /// <summary>The month chosen last, "yyyy-MM".</summary>
    public string? LastMonth { get; set; }

    public MailConfig Mail { get; set; } = new();
    public HtmlConfig Html { get; set; } = new();
    public SmtpConfig Smtp { get; set; } = new();

    /// <summary>"Light", "Dark" or "System" (follow the Windows app mode). An unknown value means System.</summary>
    public string Theme { get; set; } = "System";

    public WindowPlacement? Window { get; set; }
    public double LeftPanelWidth { get; set; } = 280;
    public bool LeftPanelVisible { get; set; } = true;
    public double RightPanelWidth { get; set; } = 320;
    public bool RightPanelVisible { get; set; } = true;
    public bool SmtpExpanded { get; set; }
}

public sealed class MailConfig
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Subject { get; set; } = MailComposer.DefaultSubject;
    public string Message { get; set; } = "Hello,\n\nHere is the report for the month. It is in the body of this message.\n";
}

public sealed class HtmlConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EmailLayout Layout { get; set; } = EmailLayout.StyledTable;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TablePages TablePages { get; set; } = TablePages.OnePage;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PageFlow PageFlow { get; set; } = PageFlow.SingleFilePaged;

    public bool AttachHtml { get; set; }
}

public sealed class SmtpConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string User { get; set; } = "";

    /// <summary>
    /// The password, in plain text: anyone who can open the folder can read it. Keep the
    /// folder to yourself, or leave the password empty for a relay that needs none.
    /// </summary>
    public string Password { get; set; } = "";
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public sealed class AppConfigStore(string folder)
{
    public const string FileName = "SharingHTMLReportsViaEmail.cfg";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>The store next to the executable.</summary>
    public static AppConfigStore Default { get; } = new(AppContext.BaseDirectory);

    public string Folder { get; } = folder;
    public string FilePath => Path.Combine(Folder, FileName);

    /// <summary>Reads the file. A missing file, or one that is not valid, gives the defaults.</summary>
    public AppConfig Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? new AppConfig()
                : new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppConfig();
        }
    }

    /// <exception cref="IOException">The folder cannot be written to.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be written to.</exception>
    public void Save(AppConfig config)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Json));
        File.Move(temp, FilePath, overwrite: true);
    }
}
