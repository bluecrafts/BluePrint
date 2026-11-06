// The Printer Profile panel on the right of the main window. There is no dialog
// and no Save button: a field is committed when the user presses Enter or leaves
// it, picks from a list or ticks a box, and the profile is then saved at once.
//
// A commit reads the whole form into a copy of the profile and checks the copy.
// Only a copy without problems is taken into the profile itself and saved, so the
// profile in memory - the one the preview and the printer use - always holds
// values that passed the checks. A field that does not pass keeps what was typed,
// and the problem is shown under the buttons until it is fixed.

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BluePrint.Render.Export.Mono;
using BluePrint.Render.Export.Mono.Printers;
using BluePrint.UI.Controls;
using ReceiptPrinting.Profiles;
using ReceiptPrinting.Transports;
using ReceiptPrinting.Transports.Discovery;

namespace ReceiptPrinting.Views;

public partial class PrinterProfilePanel : UserControl
{
    // An entry of a combo box that stands for a value.
    private sealed record Choice(object? Value, string Name);

    // The Cut box stands for two options of the encoder that overlap: CutAfter cuts
    // every page and makes CutAtEnd pointless. One box cannot be set the wrong way.
    private enum CutChoice { AtEnd, EveryPage, None }

    // 80 and 160: a dot matrix printer (Epson TM-U220) in single and double density
    private static readonly int[] CommonDpi = [80, 160, 180, 203, 300];
    private static readonly int[] CommonBaudRates = [9600, 19200, 38400, 57600, 115200];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// The control of each option of BluePrint's command encoder that the panel
    /// sets, by its path in PrinterCommandOptions. Kept next to the form so that
    /// an option BluePrint adds is either given a control or listed in
    /// <see cref="NotShown"/> (a test checks both lists against the encoder).
    /// </summary>
    public static IReadOnlyDictionary<string, string> CommandFields { get; } = new Dictionary<string, string>
    {
        ["Initialize"] = nameof(InitializeBox),
        ["Copies"] = nameof(CopiesBox),
        ["EscPos.MaxStripHeight"] = nameof(StripHeightBox),
        ["EscPos.FeedLinesAfter"] = nameof(FeedBox),
        ["EscPos.CutAfter"] = nameof(CutBox),
        ["EscPos.CutAtEnd"] = nameof(CutBox),
        ["EscPos.CutMode"] = nameof(CutTypeBox),
        ["EscPos.OpenDrawer"] = nameof(DrawerBox),
        ["EscPos.DrawerPin"] = nameof(DrawerPinBox),
        ["EscPos.DrawerOnMs"] = nameof(PulseOnBox),
        ["EscPos.DrawerOffMs"] = nameof(PulseOffBox),
        ["EscPos.ImageCommand"] = nameof(ImageCommandBox),
        ["EscPos.ColumnDensity"] = nameof(ColumnModeBox),
        ["EscPos.ColumnLineSpacing"] = nameof(LineSpacingBox),
        ["EscPos.Unidirectional"] = nameof(UnidirectionalBox),
    };

    /// <summary>Options of the encoder that the panel leaves out on purpose, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> NotShown { get; } = new Dictionary<string, string>
    {
        ["Tspl"] = "A label printer language. Receipt printers speak ESC/POS.",
        ["Zpl"] = "A label printer language. Receipt printers speak ESC/POS.",
        ["Epl"] = "A label printer language. Receipt printers speak ESC/POS.",
        ["Ezpl"] = "A label printer language. Receipt printers speak ESC/POS.",
    };

    private readonly List<string> _formProblems = [];
    private ObservableCollection<PrinterProfile> _profiles = [];
    private bool _loading;
    private bool _dragging;

    public PrinterProfilePanel()
    {
        InitializeComponent();

        ConnectionBox.ItemsSource = TransportCatalog.All;
        foreach (var dpi in CommonDpi) DpiBox.Items.Add(dpi.ToString(Invariant));
        foreach (var baud in CommonBaudRates) BaudBox.Items.Add(baud.ToString(Invariant));
        foreach (var bits in new[] { 8, 7, 6, 5 }) DataBitsBox.Items.Add(bits);

        ParityBox.ItemsSource = Choices<SerialParity>();
        StopBitsBox.ItemsSource = new[]
        {
            new Choice(SerialStopBits.One, "1"), new Choice(SerialStopBits.OnePointFive, "1.5"), new Choice(SerialStopBits.Two, "2"),
        };
        HandshakeBox.ItemsSource = new[]
        {
            new Choice(SerialHandshake.RequestToSend, "RTS/CTS"), new Choice(SerialHandshake.XOnXOff, "XON/XOFF"),
            new Choice(SerialHandshake.RequestToSendXOnXOff, "RTS/CTS + XON/XOFF"), new Choice(SerialHandshake.None, "None"),
        };
        CutBox.ItemsSource = new[]
        {
            new Choice(CutChoice.AtEnd, "At the end of the job"), new Choice(CutChoice.EveryPage, "After every page"),
            new Choice(CutChoice.None, "Do not cut"),
        };
        CutTypeBox.ItemsSource = new[] { new Choice(EscPosCutMode.Partial, "Partial"), new Choice(EscPosCutMode.Full, "Full") };
        DrawerPinBox.ItemsSource = new[] { new Choice(EscPosDrawerPin.Pin2, "Pin 2"), new Choice(EscPosDrawerPin.Pin5, "Pin 5") };
        ImageCommandBox.ItemsSource = new[]
        {
            new Choice(EscPosImageCommand.Raster, "Raster (GS v 0)"),
            new Choice(EscPosImageCommand.Column, "Column (ESC *) - dot matrix and older printers"),
        };
        ColumnModeBox.ItemsSource = new[]
        {
            new Choice(EscPosColumnDensity.Single8, "8-dot single density"), new Choice(EscPosColumnDensity.Double8, "8-dot double density"),
            new Choice(EscPosColumnDensity.Single24, "24-dot single density"), new Choice(EscPosColumnDensity.Double24, "24-dot double density"),
        };
        ImageWidthBox.ItemsSource = new[]
        {
            new Choice(ImageWidthMode.Report, "Same as the form (actual size)"),
            new Choice(ImageWidthMode.PrintHead, "Stretch to the print head"),
        };
        DitherScopeBox.ItemsSource = new[]
        {
            new Choice(MonoDitherScope.None, "None (threshold only)"), new Choice(MonoDitherScope.Images, "Images"),
            new Choice(MonoDitherScope.ImagesAndCharts, "Images and charts"), new Choice(MonoDitherScope.All, "Whole page"),
        };
        DitherAlgorithmBox.ItemsSource = new[]
        {
            new Choice(MonoDitherAlgorithm.Atkinson, "Atkinson"), new Choice(MonoDitherAlgorithm.FloydSteinberg, "Floyd-Steinberg"),
            new Choice(MonoDitherAlgorithm.Bayer4x4, "Bayer 4x4"),
        };
        NoProfilesText.SetLines(BpMessageKind.Information, ["No printer profiles yet. Create one from a preset."]);

        var menu = new ContextMenu();
        foreach (var preset in PrinterPresets.All)
        {
            var item = new MenuItem { Header = preset.Name };
            item.Click += (_, _) => AddProfile(preset.Create(), preset.Name);
            menu.Items.Add(item);
        }
        NewButton.ContextMenu = menu;

        ShowSelected();

        // The devices are looked for once the panel is on screen, and again with Refresh.
        Loaded += async (_, _) =>
        {
            if (_usbPrinters is null) await LoadDevicesAsync();
        };
    }

    // ---- what the window sees ------------------------------------------------------

    /// <summary>
    /// Writes ReceiptPrinting.cfg; false when it could not be written (the window
    /// tells the user). Called after every change that passed the checks.
    /// </summary>
    public Func<bool>? SaveConfig { get; set; }

    /// <summary>Another profile was chosen, or the chosen one was deleted.</summary>
    public event EventHandler? SelectedChanged;

    /// <summary>The chosen profile has new values: the image and the Print panel follow.</summary>
    public event EventHandler? ProfileChanged;

    /// <summary>A profile was added, deleted or renamed: lists that show the names refresh.</summary>
    public event EventHandler? ListChanged;

    /// <summary>Print Calibration Pattern was clicked: the window prints it with the chosen profile.</summary>
    public event EventHandler? CalibrationRequested;

    /// <summary>A problem appeared or went away (<see cref="HasProblems"/>).</summary>
    public event EventHandler? ProblemsChanged;

    /// <summary>The form holds a value that is not valid: nothing should be printed until it is fixed.</summary>
    public bool HasProblems { get; private set; }

    /// <summary>The profiles to show and edit; the window keeps them in its configuration.</summary>
    public void Attach(ObservableCollection<PrinterProfile> profiles, string? select)
    {
        _profiles = profiles;
        ProfileCombo.ItemsSource = profiles;
        ProfileCombo.SelectedItem =
            profiles.FirstOrDefault(p => string.Equals(p.Name, select, StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault();
        ShowSelected();
    }

    /// <summary>The chosen profile: the same one as the Profile box in the toolbar.</summary>
    public PrinterProfile? Selected
    {
        get => ProfileCombo.SelectedItem as PrinterProfile;
        set
        {
            if (!ReferenceEquals(ProfileCombo.SelectedItem, value)) ProfileCombo.SelectedItem = value;
        }
    }

    /// <summary>The groups that are open, by their key ("general", "connection"...).</summary>
    public IReadOnlyList<string> ExpandedGroups
    {
        get => Groups.Where(g => g.IsExpanded).Select(g => (string)g.Tag).ToList();
        set
        {
            foreach (var group in Groups) group.IsExpanded = value.Contains((string)group.Tag, StringComparer.OrdinalIgnoreCase);
        }
    }

    private IEnumerable<Expander> Groups => Form.Children.OfType<Expander>();

    // ---- the list ------------------------------------------------------------------

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowSelected();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var anchor = (Button)sender;
        NewButton.ContextMenu.PlacementTarget = anchor;
        NewButton.ContextMenu.Placement = PlacementMode.Bottom;
        NewButton.ContextMenu.IsOpen = true;
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } source) AddProfile(AppConfigStore.Clone(source), source.Name + " (copy)");
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        if (BpMessage.Confirm(Window.GetWindow(this), $"Delete the printer profile \"{profile.Name}\"?",
                              "Delete Printer Profile", includeCancel: false) != BpConfirmResult.Yes)
            return;

        var index = ProfileCombo.SelectedIndex;
        _profiles.Remove(profile);
        ProfileCombo.SelectedIndex = Math.Min(index, _profiles.Count - 1);
        SaveConfig?.Invoke();
        ListChanged?.Invoke(this, EventArgs.Empty);
    }

    // A new profile takes a name no other profile has: "Name", "Name (2)"... It is
    // saved at once, then General opens with the name ready to be typed over.
    private void AddProfile(PrinterProfile profile, string baseName)
    {
        var name = baseName;
        for (var n = 2; _profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"{baseName} ({n})";
        profile.Name = name;

        _profiles.Add(profile);
        ProfileCombo.SelectedItem = profile;
        SaveConfig?.Invoke();
        ListChanged?.Invoke(this, EventArgs.Empty);

        GeneralGroup.IsExpanded = true;
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---- profile -> form -----------------------------------------------------------

    private void ShowSelected()
    {
        _loading = true;
        try
        {
            var p = Selected;
            bool any = p is not null;
            FormScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            NoProfilesPanel.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            ProfileCombo.IsEnabled = _profiles.Count > 0;
            DuplicateButton.IsEnabled = any;
            DeleteButton.IsEnabled = any;
            _formProblems.Clear();
            if (p is null) return;

            NameBox.Text = p.Name;
            DpiBox.Text = Format(p.Dpi);
            VerticalDpiBox.Text = p.VerticalDpi is { } vertical ? Format(vertical) : "";
            WidthCmBox.Text = Format(p.PrintWidthMm / 10);

            var t = p.Transport;
            ConnectionBox.SelectedItem = TransportCatalog.All.First(e => e.Kind == t.Kind);
            HostBox.Text = t.Tcp.Host;
            PortBox.Text = Format(t.Tcp.Port);
            TimeoutBox.Text = Format(t.Tcp.ConnectTimeoutMs);
            ShowUsbDevices(t.Usb);
            ShowTestResult(BpMessageKind.Information, null);
            QueueBox.Text = t.SpoolerRaw.QueueName;
            SerialPortBox.Text = t.Serial.PortName;
            BaudBox.Text = Format(t.Serial.BaudRate);
            DataBitsBox.SelectedItem = t.Serial.DataBits;
            Select(ParityBox, t.Serial.Parity);
            Select(StopBitsBox, t.Serial.StopBits);
            Select(HandshakeBox, t.Serial.Handshake);

            var c = p.Commands;
            var e = c.EscPos;
            FeedBox.Text = Format(e.FeedLinesAfter);
            Select(CutBox, e.CutAfter ? CutChoice.EveryPage : e.CutAtEnd ? CutChoice.AtEnd : CutChoice.None);
            Select(CutTypeBox, e.CutMode);
            Select(ImageCommandBox, e.ImageCommand);
            StripHeightBox.Text = Format(e.MaxStripHeight);
            Select(ColumnModeBox, e.ColumnDensity);
            LineSpacingBox.Text = Format(e.ColumnLineSpacing);
            UnidirectionalBox.IsChecked = e.Unidirectional;
            InitializeBox.IsChecked = c.Initialize;
            CopiesBox.Text = Format(c.Copies);
            DrawerBox.IsChecked = e.OpenDrawer;
            Select(DrawerPinBox, e.DrawerPin);
            PulseOnBox.Text = Format(e.DrawerOnMs);
            PulseOffBox.Text = Format(e.DrawerOffMs);

            Select(ImageWidthBox, p.Raster.ImageWidth);
            ThresholdSlider.Value = p.Raster.Threshold;
            Select(DitherScopeBox, p.Raster.DitherScope);
            Select(DitherAlgorithmBox, p.Raster.DitherAlgorithm);

            ChunkBox.Text = Format(p.Pacing.ChunkBytes);
            ChunkDelayBox.Text = Format(p.Pacing.InterChunkDelayMs);
            PageDelayBox.Text = Format(p.Pacing.InterPageDelayMs);
        }
        finally
        {
            _loading = false;
            UpdateDerived(Selected);
            // A profile edited by hand in the file can hold a value the panel would
            // not have saved: it shows here, and printing waits for it to be fixed.
            ShowProblems(Selected is { } shown ? ProfileValidator.Validate(shown, _profiles).Select(x => x.ToString()) : []);
        }
    }

    // ---- devices on this computer --------------------------------------------------

    private IReadOnlyList<DiscoveredDevice>? _usbPrinters;   // null until the first search has finished
    private bool _searching;

    // Looking for devices can take a moment (SetupAPI, the spooler), so it runs in
    // the background; the USB box says so meanwhile. A source that fails just comes
    // back empty - the user can still type a queue or port name.
    private async Task LoadDevicesAsync()
    {
        if (_searching) return;
        _searching = true;
        ShowUsbDevices(Selected?.Transport.Usb);
        try
        {
            var (usb, queues, ports) = await Task.Run(() => (
                Safe(() => UsbPrinterDiscovery.Find().Select(UsbPrinterDiscovery.ToDevice).ToList()),
                Safe(() => SpoolerQueueDiscovery.Find().ToList()),
                Safe(() => SerialPortDiscovery.Find().Select(SerialPortDiscovery.ToDevice).ToList())));

            _usbPrinters = usb;
            _searching = false;
            _loading = true;
            try
            {
                QueueBox.ItemsSource = queues;
                SerialPortBox.ItemsSource = ports;
                if (Selected is { } p)
                {
                    QueueBox.Text = p.Transport.SpoolerRaw.QueueName;
                    SerialPortBox.Text = p.Transport.Serial.PortName;
                }
                ShowUsbDevices(Selected?.Transport.Usb);
            }
            finally { _loading = false; }
        }
        finally { _searching = false; }

        static List<DiscoveredDevice> Safe(Func<List<DiscoveredDevice>> find)
        {
            try { return find(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return []; }
        }
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await LoadDevicesAsync();

    // The USB printers found, plus the profile's own printer when it is not
    // plugged in now - it stays choosable, marked "(not connected)".
    private void ShowUsbDevices(UsbSettings? usb)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            if (_searching || _usbPrinters is null)
            {
                UsbDeviceBox.ItemsSource = new[] { new DiscoveredDevice("", "Searching...") };
                UsbDeviceBox.SelectedIndex = 0;
                return;
            }

            var items = _usbPrinters.ToList();
            if (usb is { DeviceInstanceId.Length: > 0 } &&
                !items.Any(d => string.Equals(d.Id, usb.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)))
                items.Insert(0, new DiscoveredDevice(usb.DeviceInstanceId,
                    usb.DeviceName.Length > 0 ? usb.DeviceName : usb.DeviceInstanceId, Connected: false));
            if (items.Count == 0) items.Add(new DiscoveredDevice("", "No devices found"));

            UsbDeviceBox.ItemsSource = items;
            UsbDeviceBox.SelectedItem = items.FirstOrDefault(d =>
                usb is not null && string.Equals(d.Id, usb.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
        }
        finally { _loading = wasLoading; }
    }

    // Opens the connection of the profile as it was saved, and closes it again.
    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;

        TestButton.IsEnabled = false;
        ShowTestResult(BpMessageKind.Information, "Connecting...");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await PrinterConnectionFactory.TestAsync(AppConfigStore.Clone(p).Transport, timeout.Token);
            ShowTestResult(BpMessageKind.Success, "Connected.");
        }
        catch (IOException ex)
        {
            ShowTestResult(BpMessageKind.Error, ex.Message);
        }
        catch (OperationCanceledException)
        {
            ShowTestResult(BpMessageKind.Error, "No answer within 15 seconds.");
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void ShowTestResult(BpMessageKind kind, string? text)
    {
        TestResult.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        TestIcon.Kind = kind;
        TestText.Text = text ?? "";
    }

    // Parts of the form that depend on other values: what is shown, what is enabled.
    private void UpdateDerived(PrinterProfile? p)
    {
        if (p is null) return;

        var kind = p.Transport.Kind;
        TcpPanel.Visibility = Shown(kind == TransportKind.Tcp);
        UsbPanel.Visibility = Shown(kind == TransportKind.Usb);
        SpoolerPanel.Visibility = Shown(kind == TransportKind.SpoolerRaw);
        SerialPanel.Visibility = Shown(kind == TransportKind.Serial);

        CutTypeBox.IsEnabled = CutBox.SelectedItem is Choice { Value: not CutChoice.None };
        DrawerFields.IsEnabled = DrawerBox.IsChecked == true;

        // Only the fields of the chosen image command are shown.
        bool column = p.Commands.EscPos.ImageCommand == EscPosImageCommand.Column;
        StripHeightLabel.Visibility = StripHeightPanel.Visibility = Shown(!column);
        ColumnModeLabel.Visibility = ColumnModeBox.Visibility = Shown(column);
        LineSpacingLabel.Visibility = LineSpacingPanel.Visibility = Shown(column);
        UnidirectionalBox.Visibility = Shown(column);
        CalibrateButton.Visibility = CalibrateHelp.Visibility = Shown(column);
        WidthDotsText.Text = p is { Dpi: > 0, PrintWidthDots: > 0 } ? $"= {Format(p.PrintWidthDots)} dots" : "";
        ThresholdText.Text = Format((int)Math.Round(ThresholdSlider.Value));
    }

    private void ShowProblems(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        Problems.SetLines(BpMessageKind.Error, list);
        bool has = list.Count > 0;
        if (has == HasProblems) return;
        HasProblems = has;
        ProblemsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- commit: form -> profile -> file ---------------------------------------------

    private void Form_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not TextBox) return;
        Commit();
        e.Handled = true;
    }

    // A text box (or the text of an editable combo box) that loses the focus.
    private void Form_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox) Commit();
    }

    private void Form_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Typing into an editable combo box selects matching entries as it goes:
        // that text is committed with the box, not on every key.
        if (e.OriginalSource is ComboBox { IsEditable: true, IsKeyboardFocusWithin: true, IsDropDownOpen: false }) return;
        Commit();
    }

    private void Form_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is CheckBox) Commit();
        else if (ReferenceEquals(e.OriginalSource, RecommendedButton) && Selected is { } p)
            ApplyPacing(p, TransportCatalog.RecommendedPacing(p.Transport.Kind));
        else if (ReferenceEquals(e.OriginalSource, CalibrateButton))
        {
            // What is typed but not yet committed is taken first; a problem stops it.
            Commit();
            if (!HasProblems) CalibrationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // The threshold follows the slider at once on screen; it is saved when the
    // thumb is let go, not at every step of a drag. Keys and clicks on the track
    // move it a step at a time and are saved right away.
    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ThresholdText.Text = Format((int)Math.Round(ThresholdSlider.Value));
        if (!_dragging) Commit();
    }

    private void ThresholdSlider_DragStarted(object sender, DragStartedEventArgs e) => _dragging = true;

    private void ThresholdSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _dragging = false;
        Commit();
    }

    /// <summary>
    /// Reads the form into a copy of the chosen profile and checks it; a copy
    /// without problems becomes the profile and is saved.
    /// </summary>
    private void Commit()
    {
        if (_loading || Selected is not { } p) return;

        _formProblems.Clear();
        var copy = AppConfigStore.Clone(p);
        ReadForm(copy);
        UpdateDerived(copy);

        var others = _profiles.Select(o => ReferenceEquals(o, p) ? copy : o).ToList();
        var problems = _formProblems.Concat(ProfileValidator.Validate(copy, others).Select(x => x.ToString())).ToList();
        ShowProblems(problems);
        if (problems.Count > 0) return;

        copy.Name = copy.Name.Trim();
        if (AppConfigStore.ToJson(copy) == AppConfigStore.ToJson(p)) return;   // nothing new: nothing to write

        bool renamed = copy.Name != p.Name;
        var previousKind = p.Transport.Kind;
        p.CopyFrom(copy);
        SaveConfig?.Invoke();

        // Show the values as they were kept: the print width rounded to whole bytes,
        // numbers without the zeros that were typed.
        ShowSelected();
        if (renamed)
        {
            ProfileCombo.Items.Refresh();
            ListChanged?.Invoke(this, EventArgs.Empty);
        }
        ProfileChanged?.Invoke(this, EventArgs.Empty);

        // A new connection kind usually wants different pacing - but the user may
        // have tuned the current values for this printer, so ask first.
        if (p.Transport.Kind != previousKind)
            Dispatcher.BeginInvoke(() => OfferRecommendedPacing(p));
    }

    private void ReadForm(PrinterProfile p)
    {
        p.Name = NameBox.Text.Trim();
        p.Dpi = ReadInt(DpiBox.Text, "Resolution", p.Dpi);
        // Empty: the dots are square. The same number as across is kept as empty too.
        p.VerticalDpi = VerticalDpiBox.Text.Trim().Length == 0 ? null : ReadInt(VerticalDpiBox.Text, "Vertical resolution", p.VerticalDpi ?? p.Dpi);
        if (p.VerticalDpi == p.Dpi) p.VerticalDpi = null;
        p.PrintWidthDots = ReadWidthCm(WidthCmBox.Text, p.Dpi, p.PrintWidthDots);

        var t = p.Transport;
        if (ConnectionBox.SelectedItem is TransportCatalog.Entry connection) t.Kind = connection.Kind;
        t.Tcp.Host = HostBox.Text.Trim();
        t.Tcp.Port = ReadInt(PortBox.Text, "Port", t.Tcp.Port);
        t.Tcp.ConnectTimeoutMs = ReadInt(TimeoutBox.Text, "Timeout", t.Tcp.ConnectTimeoutMs);
        if (UsbDeviceBox.SelectedItem is DiscoveredDevice { Id.Length: > 0 } usb)
        {
            t.Usb.DeviceInstanceId = usb.Id;
            t.Usb.DeviceName = usb.DisplayName;
        }
        t.SpoolerRaw.QueueName = QueueBox.Text.Trim();
        t.Serial.PortName = SerialPortBox.Text.Trim();
        t.Serial.BaudRate = ReadInt(BaudBox.Text, "Baud rate", t.Serial.BaudRate);
        if (DataBitsBox.SelectedItem is int bits) t.Serial.DataBits = bits;
        t.Serial.Parity = Value(ParityBox, t.Serial.Parity);
        t.Serial.StopBits = Value(StopBitsBox, t.Serial.StopBits);
        t.Serial.Handshake = Value(HandshakeBox, t.Serial.Handshake);

        var c = p.Commands;
        var e = c.EscPos;
        e.FeedLinesAfter = ReadInt(FeedBox.Text, "Feed after image", e.FeedLinesAfter);
        switch (Value(CutBox, CutChoice.AtEnd))
        {
            case CutChoice.AtEnd: (e.CutAfter, e.CutAtEnd) = (false, true); break;
            case CutChoice.EveryPage: (e.CutAfter, e.CutAtEnd) = (true, false); break;
            default: (e.CutAfter, e.CutAtEnd) = (false, false); break;
        }
        e.CutMode = Value(CutTypeBox, e.CutMode);
        e.ImageCommand = Value(ImageCommandBox, e.ImageCommand);
        e.MaxStripHeight = ReadInt(StripHeightBox.Text, "Strip height", e.MaxStripHeight);
        e.ColumnDensity = Value(ColumnModeBox, e.ColumnDensity);
        e.ColumnLineSpacing = ReadInt(LineSpacingBox.Text, "Line spacing", e.ColumnLineSpacing);
        e.Unidirectional = UnidirectionalBox.IsChecked == true;
        c.Initialize = InitializeBox.IsChecked == true;
        c.Copies = ReadInt(CopiesBox.Text, "Copies", c.Copies);
        e.OpenDrawer = DrawerBox.IsChecked == true;
        e.DrawerPin = Value(DrawerPinBox, e.DrawerPin);
        e.DrawerOnMs = ReadInt(PulseOnBox.Text, "Pulse on", e.DrawerOnMs);
        e.DrawerOffMs = ReadInt(PulseOffBox.Text, "Pulse off", e.DrawerOffMs);

        p.Raster.ImageWidth = Value(ImageWidthBox, p.Raster.ImageWidth);
        p.Raster.Threshold = (int)Math.Round(ThresholdSlider.Value);
        p.Raster.DitherScope = Value(DitherScopeBox, p.Raster.DitherScope);
        p.Raster.DitherAlgorithm = Value(DitherAlgorithmBox, p.Raster.DitherAlgorithm);

        p.Pacing.ChunkBytes = ReadInt(ChunkBox.Text, "Chunk size", p.Pacing.ChunkBytes);
        p.Pacing.InterChunkDelayMs = ReadInt(ChunkDelayBox.Text, "Delay between chunks", p.Pacing.InterChunkDelayMs);
        p.Pacing.InterPageDelayMs = ReadInt(PageDelayBox.Text, "Delay between pages", p.Pacing.InterPageDelayMs);
    }

    private void OfferRecommendedPacing(PrinterProfile p)
    {
        var recommended = TransportCatalog.RecommendedPacing(p.Transport.Kind);
        if (recommended.ChunkBytes == p.Pacing.ChunkBytes &&
            recommended.InterChunkDelayMs == p.Pacing.InterChunkDelayMs &&
            recommended.InterPageDelayMs == p.Pacing.InterPageDelayMs)
            return;

        if (BpMessage.Confirm(Window.GetWindow(this), $"Use the recommended pacing for {TransportCatalog.NameOf(p.Transport.Kind)}?",
                              "Pacing", includeCancel: false) == BpConfirmResult.Yes)
            ApplyPacing(p, recommended);
    }

    private void ApplyPacing(PrinterProfile p, PacingSettings pacing)
    {
        p.Pacing = pacing;
        SaveConfig?.Invoke();
        if (ReferenceEquals(Selected, p)) ShowSelected();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- print width: typed in cm, kept in dots ------------------------------------

    // Users know the width of the head in cm, not in dots. The dots are worked out
    // from the resolution and rounded to a multiple of 8 (the raster is sent a byte
    // at a time); the box then shows the cm those dots really are.
    private int ReadWidthCm(string text, int dpi, int current)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, Invariant, out var cm) || cm < 0)
        {
            _formProblems.Add("Print width: Enter a number of centimetres.");
            return current;
        }
        if (dpi <= 0) return current;

        var dots = (int)Math.Round(cm / 2.54 * dpi / 8, MidpointRounding.AwayFromZero) * 8;
        return cm > 0 ? Math.Max(8, dots) : 0;
    }

    // ---- helpers ---------------------------------------------------------------------

    private int ReadInt(string text, string field, int current)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, Invariant, out var value)) return value;
        _formProblems.Add($"{field}: Enter a whole number.");
        return current;
    }

    private static string Format(int value) => value.ToString(Invariant);
    private static string Format(double value) => value.ToString("0.###", Invariant);

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private static Choice[] Choices<T>() where T : struct, Enum
        => Enum.GetValues<T>().Select(v => new Choice(v, v.ToString())).ToArray();

    private static void Select(ComboBox box, object? value)
        => box.SelectedItem = box.Items.OfType<Choice>().FirstOrDefault(c => Equals(c.Value, value));

    private static T Value<T>(ComboBox box, T current) => box.SelectedItem is Choice { Value: T value } ? value : current;
}
