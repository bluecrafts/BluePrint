// The right panel and the Email tab: who gets the message, how the HTML is laid out,
// what it weighs, and the three ways to send it.
//
// A change of the message or of an HTML option exports the report again from the
// pages already rendered (300 ms after the last change): nothing is read from the
// database again. The fields are saved to the settings when they lose the focus.

using System.IO;
using System.Net.Mail;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BluePrint.UI.Controls;
using Microsoft.Win32;
using SharingHtmlReports.Mail;

namespace SharingHtmlReports;

public partial class MainWindow
{
    private readonly DispatcherTimer _emailTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? _emailCts;
    private CancellationTokenSource? _sendCts;
    private bool _loadingFields;
    private string _password = "";

    // The subject with {Report} and {Month} (saved in the settings). The box shows it filled in
    // for the report and month on screen, and is filled in again when they change.
    private string _subjectTemplate = MailComposer.DefaultSubject;

    /// <summary>The body of the message for the report on screen, or null.</summary>
    public EmailBody? Email { get; private set; }

    private static string TempFolder => Path.Combine(Path.GetTempPath(), "SharingHTMLReportsViaEmail");
    private const string PreviewImages = "email-preview_files";

    private void InitializeEmail()
    {
        _loadingFields = true;
        FromBox.Text = _config.Mail.From;
        ToBox.Text = _config.Mail.To;
        CcBox.Text = _config.Mail.Cc;
        _subjectTemplate = string.IsNullOrWhiteSpace(_config.Mail.Subject) ? MailComposer.DefaultSubject : _config.Mail.Subject;
        SubjectBox.Text = _subjectTemplate;
        MessageTextBox.Text = _config.Mail.Message;

        (_config.Html.Layout switch
        {
            EmailLayout.PlainTable => PlainTableRadio,
            EmailLayout.Absolute => AbsoluteRadio,
            _ => StyledTableRadio,
        }).IsChecked = true;
        (_config.Html.TablePages == TablePages.PerPage ? PerPageRadio : OnePageRadio).IsChecked = true;
        PageFlowCombo.SelectedIndex = _config.Html.PageFlow == PageFlow.SingleFile ? 0 : 1;
        AttachHtmlCheck.IsChecked = _config.Html.AttachHtml;

        HostBox.Text = _config.Smtp.Host;
        PortBox.Text = _config.Smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SecurityCombo.SelectedIndex = _config.Smtp.Security == SmtpSecurity.None ? 1 : 0;
        UserBox.Text = _config.Smtp.User;
        _password = _config.Smtp.Password ?? "";   // "password": null in a file written by hand
        PasswordBox.Password = _password;
        SmtpExpander.IsExpanded = _config.SmtpExpanded;
        _loadingFields = false;

        _emailTimer.Tick += async (_, _) =>
        {
            _emailTimer.Stop();
            await RebuildEmailNow();
        };
        UpdateOptionStates();
        UpdateMailChecks();
        UpdateSmtpChecks();
        ClearEmail();
    }

    // ---- reading the panel ----------------------------------------------------------

    /// <summary>The HTML options chosen in the panel.</summary>
    public EmailHtmlOptions HtmlOptions => new(
        PlainTableRadio.IsChecked == true ? EmailLayout.PlainTable
            : AbsoluteRadio.IsChecked == true ? EmailLayout.Absolute
            : EmailLayout.StyledTable,
        PerPageRadio.IsChecked == true ? TablePages.PerPage : TablePages.OnePage,
        PageFlowCombo.SelectedIndex == 0 ? PageFlow.SingleFile : PageFlow.SingleFilePaged);

    private SmtpSettings Smtp => new()
    {
        Host = HostBox.Text.Trim(),
        Port = int.TryParse(PortBox.Text.Trim(), out var port) ? port : 0,
        Security = SecurityCombo.SelectedIndex == 1 ? SmtpSecurity.None : SmtpSecurity.StartTls,
        User = UserBox.Text.Trim(),
        Password = _password,
    };

    // What the box shows: the subject as it is sent.
    private string Subject => string.IsNullOrWhiteSpace(SubjectBox.Text) && Shown is { } shown
        ? MailComposer.ExpandSubject(_subjectTemplate, shown.Report.Name, shown.Month.Name)
        : SubjectBox.Text.Trim();

    /// <summary>Fills the subject in again for the report and month on screen (after a retrieve).</summary>
    private void ApplySubjectTemplate()
    {
        if (Shown is not { } shown) return;
        _loadingFields = true;
        SubjectBox.Text = MailComposer.ExpandSubject(_subjectTemplate, shown.Report.Name, shown.Month.Name);
        _loadingFields = false;
    }

    /// <summary>Copies the fields into the settings (without writing the file).</summary>
    private void CommitFields()
    {
        _config.Mail.From = FromBox.Text.Trim();
        _config.Mail.To = ToBox.Text.Trim();
        _config.Mail.Cc = CcBox.Text.Trim();
        _config.Mail.Subject = _subjectTemplate;
        _config.Mail.Message = MessageTextBox.Text;
        var options = HtmlOptions;
        _config.Html.Layout = options.Layout;
        _config.Html.TablePages = options.TablePages;
        _config.Html.PageFlow = options.PageFlow;
        _config.Html.AttachHtml = AttachHtmlCheck.IsChecked == true;
        _config.Smtp.Host = HostBox.Text.Trim();
        if (int.TryParse(PortBox.Text.Trim(), out var port)) _config.Smtp.Port = port;
        _config.Smtp.Security = Smtp.Security;
        _config.Smtp.User = UserBox.Text.Trim();
        _config.Smtp.Password = _password;
        _config.SmtpExpanded = SmtpExpander.IsExpanded;
    }

    // ---- field events ---------------------------------------------------------------

    private void MailField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingFields) return;
        // A subject typed by the user: keep it as a template, so the next report or month fills it in.
        if (ReferenceEquals(sender, SubjectBox) && Shown is { } shown)
            _subjectTemplate = string.IsNullOrWhiteSpace(SubjectBox.Text)
                ? MailComposer.DefaultSubject
                : MailComposer.ToTemplate(SubjectBox.Text.Trim(), shown.Report.Name, shown.Month.Name);
        UpdateMailChecks();
        if (ReferenceEquals(sender, MessageTextBox) || ReferenceEquals(sender, SubjectBox)) ScheduleEmail();
    }

    private void MailField_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitFields();
        SaveConfig();
    }

    private void HtmlOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingFields || !IsInitialized) return;
        UpdateOptionStates();
        CommitFields();
        SaveConfig();
        ScheduleEmail();
    }

    private void SmtpField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingFields) UpdateSmtpChecks();
    }

    private void SmtpField_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingFields) return;
        UpdateSmtpChecks();
        CommitFields();
        SaveConfig();
    }

    private void SmtpField_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitFields();
        SaveConfig();
    }

    // Saved at once: a password box has no "commit" of its own.
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _password = PasswordBox.Password;
        if (_loadingFields) return;
        _config.Smtp.Password = _password;
        SaveConfig();
    }

    private void SmtpExpander_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingFields) return;
        _config.SmtpExpanded = SmtpExpander.IsExpanded;
        SaveConfig();
    }

    // Table pages only mean something for a table; the export mode only for the absolute layout.
    private void UpdateOptionStates()
    {
        bool table = HtmlOptions.IsTable;
        TablePagesPanel.IsEnabled = TablePagesLabel.IsEnabled = table;
        TablePagesPanel.ToolTip = table ? null : "Only for Styled table and Plain table.";
        PageFlowCombo.IsEnabled = PageFlowLabel.IsEnabled = !table;
        PageFlowCombo.ToolTip = table ? "Only for Absolute: a table is one long page or one table per page." : null;
    }

    private void UpdateMailChecks()
    {
        var problems = new List<string>();
        void Check(string label, TextBox box)
        {
            if (!MailComposer.TryParseAddresses(box.Text, out var list, out var error)) problems.Add($"{label}: {error}");
            else if (label == "From" && list.Count > 1) problems.Add("From: give one sender.");
        }
        Check("From", FromBox);
        Check("To", ToBox);
        Check("Cc", CcBox);
        MailProblemLines.SetLines(BpMessageKind.Error, problems);
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateSmtpChecks()
    {
        var problem = MailSender.Problem(Smtp);
        SmtpProblemLines.SetLines(BpMessageKind.Warning, problem is null ? [] : [problem]);
        CommandManager.InvalidateRequerySuggested();
    }

    // ---- the body -------------------------------------------------------------------

    private void ScheduleEmail()
    {
        _emailTimer.Stop();
        _emailTimer.Start();
    }

    /// <summary>Exports the report on screen again with the options of the panel.</summary>
    public async Task RebuildEmailNow()
    {
        _emailTimer.Stop();
        _emailCts?.Cancel();
        var cts = _emailCts = new CancellationTokenSource();
        if (Shown is not { } shown)
        {
            ClearEmail();
            return;
        }

        var options = HtmlOptions;
        string subject = Subject;
        string message = MessageTextBox.Text;
        string footer = $"{shown.Title} - {shown.Rendered.Pages.Count} page(s). Made with BluePrint.";
        EmailSizeText.Text = "Updating...";
        var body = await Task.Run(() => HtmlEmailBuilder.Build(shown.Rendered, options, subject, message, footer, shown.Rows));
        if (cts.IsCancellationRequested || !ReferenceEquals(shown, Shown)) return;

        Email = body;
        EmailSummaryText.Text = body.SummaryText;
        EmailNotesLines.SetLines(body.Notes.Select(n =>
            (n.Kind == EmailNoteKind.Warning ? BpMessageKind.Warning : BpMessageKind.Information, n.Text)));
        EmailSizeText.Text = body.SizeText;
        EmailSizeIcon.Visibility = body.Notes.Any(n => n.Kind == EmailNoteKind.Warning) ? Visibility.Visible : Visibility.Collapsed;
        EmailSizeIcon.ToolTip = string.Join("\n", body.Notes.Select(n => n.Text));
        UpdateMailChecks();
        ShowEmailHeader();
        ShowInEmailView(body);
    }

    private void ClearEmail()
    {
        Email = null;
        EmailSummaryText.Text = "";
        EmailNotesLines.SetLines(BpMessageKind.Information, []);
        EmailSizeText.Text = "";
        EmailSizeIcon.Visibility = Visibility.Collapsed;
        EmailHeaderText.Text = "";
        EmailView.Visibility = Visibility.Collapsed;
        EmailEmptyText.Visibility = Visibility.Visible;
    }

    private void ShowEmailHeader()
    {
        var to = string.IsNullOrWhiteSpace(ToBox.Text) ? "(no recipient)" : ToBox.Text.Trim();
        EmailHeaderText.Text = $"To: {to}    Subject: {Subject}";
    }

    // The Windows web control shows a local file in an old document mode unless the file
    // asks for the newest: only the preview copy gets that line, not the message sent.
    // A web page cannot read cid: pictures: the charts go into a folder next to it.
    private void ShowInEmailView(EmailBody body)
    {
        Directory.CreateDirectory(TempFolder);
        var path = Path.Combine(TempFolder, "email-preview.html");
        body.WriteImages(Path.Combine(TempFolder, PreviewImages));
        var html = body.HtmlWithFolder(PreviewImages);
        int head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        var preview = head < 0 ? html
            : html.Insert(head + 6, "\n<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
        File.WriteAllText(path, preview, new System.Text.UTF8Encoding(false));
        EmailEmptyText.Visibility = Visibility.Collapsed;
        EmailView.Visibility = Visibility.Visible;
        EmailView.Navigate(new Uri(path));
    }

    private void PreviewTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PreviewTabs) && PreviewTabs.SelectedItem == EmailTab) ShowEmailHeader();
    }

    // ---- the message ----------------------------------------------------------------

    private bool AddressesValid(bool needFrom)
    {
        if (!MailComposer.TryParseAddresses(FromBox.Text, out var from, out _) || from.Count > 1) return false;
        if (needFrom && from.Count == 0) return false;
        if (!MailComposer.TryParseAddresses(ToBox.Text, out var to, out _) || to.Count == 0) return false;
        return MailComposer.TryParseAddresses(CcBox.Text, out _, out _);
    }

    /// <summary>The message for the report on screen, as it would be sent now.</summary>
    public MailMessage ComposeMessage()
    {
        var shown = Shown ?? throw new InvalidOperationException("There is no report to send.");
        var body = Email ?? throw new InvalidOperationException("The message is not ready yet.");
        var subject = Subject;
        return MailComposer.Compose(new MailInput
        {
            From = FromBox.Text,
            To = ToBox.Text,
            Cc = CcBox.Text,
            Subject = subject,
            Message = MessageTextBox.Text,
            Body = body,
            ReportTitle = shown.Title,
            AttachmentHtml = AttachHtmlCheck.IsChecked == true ? HtmlEmailBuilder.BuildAttachment(shown.Rendered, subject) : null,
            AttachmentName = MailComposer.FileNameFor(shown.Report.Name, shown.Month.Key) + ".html",
        });
    }

    private string FileName => Shown is { } shown ? MailComposer.FileNameFor(shown.Report.Name, shown.Month.Key) : "report";

    private void HasEmail_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Email is not null;

    private void CanSaveEml_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = Email is not null && _sendCts is null && AddressesValid(needFrom: false);

    private void CanSend_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (!IsInitialized) return;   // asked while InitializeComponent is still building the window
        e.CanExecute = Email is not null && _sendCts is null && AddressesValid(needFrom: true) && MailSender.Problem(Smtp) is null;
        SendButton.ToolTip = e.CanExecute ? "Send the message through the SMTP server (Ctrl+Enter)" : SendBlockedReason();
    }

    private string SendBlockedReason()
    {
        if (Email is null) return "Select a report and a month first.";
        if (_sendCts is not null) return "The message is being sent.";
        if (!MailComposer.TryParseAddresses(FromBox.Text, out var from, out _) || from.Count != 1) return "Enter one sender in From.";
        if (!MailComposer.TryParseAddresses(ToBox.Text, out var to, out _) || to.Count == 0) return "Enter at least one recipient in To.";
        if (!MailComposer.TryParseAddresses(CcBox.Text, out _, out _)) return "Correct the addresses in Cc.";
        return MailSender.Problem(Smtp) ?? "";
    }

    private async void Send_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Email is not { } body) return;
        if (body.Bytes > HtmlEmailBuilder.LargeMessageBytes &&
            !BpMessage.Ask(this, $"The message body is {body.SizeText}. Many mail servers refuse large messages.\n\nSend it anyway?",
                           title: "Send"))
            return;

        CommitFields();
        SaveConfig();
        using var message = ComposeMessage();
        int recipients = message.To.Count + message.CC.Count;
        var cts = _sendCts = new CancellationTokenSource();
        SetSending(true);
        SetStatus($"Sending to {recipients} recipient(s)...");
        try
        {
            await MailSender.SendAsync(message, Smtp, cts.Token);
            SetStatus($"Sent \"{message.Subject}\" to {recipients} recipient(s) ({body.SizeText}).", BpMessageKind.Success);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Sending cancelled.", BpMessageKind.Warning);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException or FormatException)
        {
            SetStatus($"Sending failed: {ex.Message}", BpMessageKind.Error);
            var detail = ex is SmtpException smtp ? $"{smtp.StatusCode}: {ex}" : ex.ToString();
            BpMessage.ShowDetails(this,
                $"The message was not sent.\n\n{ex.Message}\n\nCheck the SMTP server settings, or save the message " +
                "as an .eml file and send it from your mail program.",
                detail, BpMessageKind.Error, title: "Send");
        }
        finally
        {
            _sendCts = null;
            cts.Dispose();
            SetSending(false);
        }
    }

    private void SetSending(bool sending)
    {
        SendProgress.Visibility = sending ? Visibility.Visible : Visibility.Collapsed;
        SendButton.Visibility = sending ? Visibility.Collapsed : Visibility.Visible;
        CancelSendButton.Visibility = sending ? Visibility.Visible : Visibility.Collapsed;
        CommandManager.InvalidateRequerySuggested();
    }

    private void CancelSend_Click(object sender, RoutedEventArgs e) => _sendCts?.Cancel();

    private void SaveEml_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save .eml",
            Filter = "Email message (*.eml)|*.eml",
            FileName = FileName + ".eml",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var message = ComposeMessage();
            MailSender.SaveEml(message, dialog.FileName);
            SetStatus($"Saved {Path.GetFileName(dialog.FileName)}.", BpMessageKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SmtpException or FormatException)
        {
            SetStatus($"Could not save the message: {ex.Message}", BpMessageKind.Error);
            BpMessage.Show(this, $"Could not save the message.\n\n{ex.Message}", BpMessageKind.Error, title: "Save .eml");
        }
    }

    private void OpenInMailApp_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            using var message = ComposeMessage();
            MailSender.OpenInMailApp(message, FileName);
            SetStatus("Opened the message in your mail program.", BpMessageKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SmtpException or FormatException
                                       or System.ComponentModel.Win32Exception)
        {
            SetStatus($"Could not open the message: {ex.Message}", BpMessageKind.Error);
            BpMessage.Show(this,
                $"Could not open the message in a mail program.\n\n{ex.Message}\n\nUse Save .eml and open the file yourself.",
                BpMessageKind.Error, title: "Open in Mail App");
        }
    }

    private void OpenInBrowser_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Email is not { } body) return;
        try
        {
            Directory.CreateDirectory(TempFolder);
            var path = Path.Combine(TempFolder, FileName + ".html");
            var images = FileName + "_files";
            body.WriteImages(Path.Combine(TempFolder, images));
            File.WriteAllText(path, body.HtmlWithFolder(images), new System.Text.UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            BpMessage.Show(this, $"Could not open the web browser.\n\n{ex.Message}", BpMessageKind.Error, title: "Open in Browser");
        }
    }
}
