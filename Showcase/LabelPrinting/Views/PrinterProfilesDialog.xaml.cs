// Creating, editing and removing printer profiles. Everything is done on copies;
// Result holds them once the user presses Save, and nothing changes on Cancel.
//
// The form is filled from the selected profile and read back into it on every
// change (no data binding): a number field that does not hold a number is
// reported under the form instead of silently keeping the old value.

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
using LabelPrinting.Profiles;
using LabelPrinting.Transports;
using LabelPrinting.Transports.Discovery;

namespace LabelPrinting.Views;

public partial class PrinterProfilesDialog : BpDialogWindow
{
    // An entry of a combo box that stands for a value; Value may be null ("printer default").
    private sealed record Choice(object? Value, string Name);

    private static readonly int[] CommonDpi = [180, 203, 300, 600];
    private static readonly int[] CommonBaudRates = [9600, 19200, 38400, 57600, 115200];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly ObservableCollection<PrinterProfile> _profiles;
    private readonly List<string> _formProblems = [];
    private bool _loading;

    /// <summary>The profiles after Save; null when the dialog was cancelled.</summary>
    public List<PrinterProfile>? Result { get; private set; }

    /// <summary>The profile selected when Save was pressed, for the main window to select.</summary>
    public string? SelectedName { get; private set; }

    /// <param name="tab">The header of the tab to open at (e.g. "Image"); null or unknown opens the first.</param>
    public PrinterProfilesDialog(IEnumerable<PrinterProfile> profiles, string? selectName, string? tab = null)
    {
        InitializeComponent();
        if (Form.Items.OfType<TabItem>().FirstOrDefault(t => Equals(t.Header, tab)) is { } start)
            Form.SelectedItem = start;

        _profiles = new(profiles.Select(AppConfigStore.Clone));
        ProfileList.ItemsSource = _profiles;

        CommandBox.ItemsSource = PrinterCommandCatalog.All;
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
        DirectionBox.ItemsSource = new[] { new Choice(0, "0"), new Choice(1, "1 (rotate 180)") };
        CompressionBox.ItemsSource = new[] { new Choice(ZplCompression.Z64, "Z64"), new Choice(ZplCompression.Hex, "Hex") };
        PrintModeBox.ItemsSource = new[]
        {
            new Choice(null, "Printer default"), new Choice(ZplPrintMode.TearOff, "Tear-off"),
            new Choice(ZplPrintMode.PeelOff, "Peel-off"), new Choice(ZplPrintMode.Rewind, "Rewind"),
            new Choice(ZplPrintMode.Cutter, "Cutter"), new Choice(ZplPrintMode.Applicator, "Applicator"),
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
        // The published files are ASCII only, so the degree sign is made from its code.
        var degrees = $"180{(char)0xB0}";
        OrientationBox.ItemsSource = new[]
        {
            new Choice(MonoOrientation.Portrait, "Portrait"), new Choice(MonoOrientation.Landscape, "Landscape"),
            new Choice(MonoOrientation.Portrait180, $"Portrait {degrees}"), new Choice(MonoOrientation.Landscape180, $"Landscape {degrees}"),
        };

        var menu = new ContextMenu();
        foreach (var preset in PrinterPresets.All)
        {
            var item = new MenuItem { Header = preset.Name };
            item.Click += (_, _) => AddProfile(preset.Create(), preset.Name);
            menu.Items.Add(item);
        }
        NewButton.ContextMenu = menu;

        ProfileList.SelectedItem =
            _profiles.FirstOrDefault(p => string.Equals(p.Name, selectName, StringComparison.OrdinalIgnoreCase))
            ?? _profiles.FirstOrDefault();
        ShowSelected();

        Loaded += async (_, _) => await LoadDevicesAsync();
    }

    // ---- devices on this computer --------------------------------------------

    private IReadOnlyList<DiscoveredDevice>? _usbPrinters;   // null until the first search has finished
    private bool _searching;

    // Looking for devices can take a moment (SetupAPI, the spooler), so it runs
    // in the background; the combo boxes say so meanwhile. A source that fails
    // just comes back empty - the user can still type a queue or port name.
    private async Task LoadDevicesAsync()
    {
        if (_searching) return;
        _searching = true;
        ShowUsbDevices();
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
                ShowUsbDevices();
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
    private void ShowUsbDevices()
    {
        if (_searching || _usbPrinters is null)
        {
            UsbDeviceBox.ItemsSource = new[] { new DiscoveredDevice("", "Searching...") };
            UsbDeviceBox.SelectedIndex = 0;
            return;
        }

        var items = _usbPrinters.ToList();
        var usb = Selected?.Transport.Usb;
        if (usb is { DeviceInstanceId.Length: > 0 } &&
            !items.Any(d => string.Equals(d.Id, usb.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)))
            items.Insert(0, new DiscoveredDevice(usb.DeviceInstanceId,
                usb.DeviceName.Length > 0 ? usb.DeviceName : usb.DeviceInstanceId, Connected: false));
        if (items.Count == 0) items.Add(new DiscoveredDevice("", "No devices found"));

        UsbDeviceBox.ItemsSource = items;
        UsbDeviceBox.SelectedItem = items.FirstOrDefault(d =>
            usb is not null && string.Equals(d.Id, usb.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
    }

    // Opens the connection the form describes and closes it again.
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

    private PrinterProfile? Selected => ProfileList.SelectedItem as PrinterProfile;

    // ---- the list ------------------------------------------------------------

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        NewButton.ContextMenu.PlacementTarget = NewButton;
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
        if (BpMessage.Confirm(this, $"Delete the printer profile \"{profile.Name}\"?", "Delete Printer Profile",
                              includeCancel: false) != BpConfirmResult.Yes)
            return;

        var index = ProfileList.SelectedIndex;
        _profiles.Remove(profile);
        ProfileList.SelectedIndex = Math.Min(index, _profiles.Count - 1);
        ShowSelected();
    }

    // A new profile takes a name no other profile has: "Name", "Name (2)"...
    private void AddProfile(PrinterProfile profile, string baseName)
    {
        var name = baseName;
        for (var n = 2; _profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"{baseName} ({n})";
        profile.Name = name;

        _profiles.Add(profile);
        ProfileList.SelectedItem = profile;
        ShowSelected();
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    // ---- profile -> form -----------------------------------------------------

    private void ShowSelected()
    {
        _loading = true;
        try
        {
            var p = Selected;
            Form.IsEnabled = p is not null;
            DuplicateButton.IsEnabled = p is not null;
            DeleteButton.IsEnabled = p is not null;
            _formProblems.Clear();
            if (p is null) return;

            NameBox.Text = p.Name;
            CommandBox.SelectedItem = PrinterCommandCatalog.Of(p.PrinterCommand);
            DpiBox.Text = Format(p.Dpi);
            WidthCmBox.Text = Format(p.PrintWidthMm / 10);
            ShowWidthDots(p);
            RowWidthBox.Text = FormatCm(p.Label.WidthMm);
            RowHeightBox.Text = FormatCm(p.Label.HeightMm);

            var t = p.Transport;
            ConnectionBox.SelectedItem = TransportCatalog.All.First(e => e.Kind == t.Kind);
            HostBox.Text = t.Tcp.Host;
            PortBox.Text = Format(t.Tcp.Port);
            TimeoutBox.Text = Format(t.Tcp.ConnectTimeoutMs);
            ShowUsbDevices();
            ShowTestResult(BpMessageKind.Information, null);
            QueueBox.Text = t.SpoolerRaw.QueueName;
            SerialPortBox.Text = t.Serial.PortName;
            BaudBox.Text = Format(t.Serial.BaudRate);
            DataBitsBox.SelectedItem = t.Serial.DataBits;
            Select(ParityBox, t.Serial.Parity);
            Select(StopBitsBox, t.Serial.StopBits);
            Select(HandshakeBox, t.Serial.Handshake);

            ChunkBox.Text = Format(p.Pacing.ChunkBytes);
            ChunkDelayBox.Text = Format(p.Pacing.InterChunkDelayMs);
            RowDelayBox.Text = Format(p.Pacing.InterPageDelayMs);

            var c = p.Commands;
            InitializeBox.IsChecked = c.Initialize;
            CopiesBox.Text = Format(c.Copies);
            TsplGapBox.Text = Format(c.Tspl.GapMm);
            Select(DirectionBox, c.Tspl.Direction);
            TsplDensityBox.Text = Format(c.Tspl.Density);
            TsplSpeedBox.Text = Format(c.Tspl.Speed);
            Select(CompressionBox, c.Zpl.Compression);
            HomeXBox.Text = Format(c.Zpl.LabelHomeX);
            HomeYBox.Text = Format(c.Zpl.LabelHomeY);
            Select(PrintModeBox, c.Zpl.PrintMode);
            EplGapBox.Text = Format(c.Epl.GapDots);
            EplDarknessBox.Text = Format(c.Epl.Darkness);
            EplSpeedBox.Text = Format(c.Epl.Speed);
            EzplGapBox.Text = Format(c.Ezpl.GapMm);
            EzplDarknessBox.Text = Format(c.Ezpl.Darkness);
            EzplSpeedBox.Text = Format(c.Ezpl.Speed);

            ThresholdSlider.Value = p.Raster.Threshold;
            Select(DitherScopeBox, p.Raster.DitherScope);
            Select(DitherAlgorithmBox, p.Raster.DitherAlgorithm);
            Select(OrientationBox, p.Raster.Orientation);
            MirrorBox.IsChecked = p.Raster.Mirror;
            NegativeBox.IsChecked = p.Raster.Negative;
        }
        finally
        {
            _loading = false;
            UpdateDerived();
            Validate();
        }
    }

    // ---- form -> profile -----------------------------------------------------

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || Selected is not { } p) return;

        var previousKind = p.Transport.Kind;
        _formProblems.Clear();

        p.Name = NameBox.Text.Trim();
        if (CommandBox.SelectedItem is PrinterCommandCatalog.Entry command) p.PrinterCommand = command.Language;
        p.Dpi = ReadInt(DpiBox.Text, "Resolution", p.Dpi);
        p.PrintWidthDots = ReadWidthCm(WidthCmBox.Text, p.Dpi, p.PrintWidthDots);
        p.Label.WidthMm = ReadCm(RowWidthBox.Text, "Row width", p.Label.WidthMm);
        p.Label.HeightMm = ReadCm(RowHeightBox.Text, "Row height", p.Label.HeightMm);

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

        p.Pacing.ChunkBytes = ReadInt(ChunkBox.Text, "Chunk size", p.Pacing.ChunkBytes);
        p.Pacing.InterChunkDelayMs = ReadInt(ChunkDelayBox.Text, "Delay between chunks", p.Pacing.InterChunkDelayMs);
        p.Pacing.InterPageDelayMs = ReadInt(RowDelayBox.Text, "Delay between rows", p.Pacing.InterPageDelayMs);

        var c = p.Commands;
        c.Initialize = InitializeBox.IsChecked == true;
        c.Copies = ReadInt(CopiesBox.Text, "Copies", c.Copies);
        c.Tspl.GapMm = ReadOptionalDouble(TsplGapBox.Text, "TSPL gap", c.Tspl.GapMm);
        c.Tspl.Direction = Value(DirectionBox, c.Tspl.Direction);
        c.Tspl.Density = ReadOptionalInt(TsplDensityBox.Text, "TSPL density", c.Tspl.Density);
        c.Tspl.Speed = ReadOptionalDouble(TsplSpeedBox.Text, "TSPL speed", c.Tspl.Speed);
        c.Zpl.Compression = Value(CompressionBox, c.Zpl.Compression);
        c.Zpl.LabelHomeX = ReadInt(HomeXBox.Text, "Label home X", c.Zpl.LabelHomeX);
        c.Zpl.LabelHomeY = ReadInt(HomeYBox.Text, "Label home Y", c.Zpl.LabelHomeY);
        c.Zpl.PrintMode = OptionalValue(PrintModeBox, c.Zpl.PrintMode);
        c.Epl.GapDots = ReadOptionalInt(EplGapBox.Text, "EPL2 gap", c.Epl.GapDots);
        c.Epl.Darkness = ReadOptionalInt(EplDarknessBox.Text, "EPL2 darkness", c.Epl.Darkness);
        c.Epl.Speed = ReadOptionalInt(EplSpeedBox.Text, "EPL2 speed", c.Epl.Speed);
        c.Ezpl.GapMm = ReadOptionalDouble(EzplGapBox.Text, "EZPL gap", c.Ezpl.GapMm);
        c.Ezpl.Darkness = ReadOptionalInt(EzplDarknessBox.Text, "EZPL darkness", c.Ezpl.Darkness);
        c.Ezpl.Speed = ReadOptionalInt(EzplSpeedBox.Text, "EZPL speed", c.Ezpl.Speed);

        p.Raster.Threshold = (int)Math.Round(ThresholdSlider.Value);
        p.Raster.DitherScope = Value(DitherScopeBox, p.Raster.DitherScope);
        p.Raster.DitherAlgorithm = Value(DitherAlgorithmBox, p.Raster.DitherAlgorithm);
        p.Raster.Orientation = Value(OrientationBox, p.Raster.Orientation);
        p.Raster.Mirror = MirrorBox.IsChecked == true;
        p.Raster.Negative = NegativeBox.IsChecked == true;

        if (ReferenceEquals(e.OriginalSource, NameBox)) ProfileList.Items.Refresh();
        // The dots appear once the width is committed (Enter / leaving the box),
        // not on every key; a new resolution changes them right away.
        if (!ReferenceEquals(e.OriginalSource, WidthCmBox)) ShowWidthDots(p);
        UpdateDerived();
        Validate();

        // A new connection kind usually wants different pacing - but the user may
        // have tuned the current values for this printer, so ask first.
        if (t.Kind != previousKind)
            Dispatcher.BeginInvoke(() => OfferRecommendedPacing(p));
    }

    private void OfferRecommendedPacing(PrinterProfile p)
    {
        var recommended = TransportCatalog.RecommendedPacing(p.Transport.Kind);
        if (recommended.ChunkBytes == p.Pacing.ChunkBytes &&
            recommended.InterChunkDelayMs == p.Pacing.InterChunkDelayMs &&
            recommended.InterPageDelayMs == p.Pacing.InterPageDelayMs)
            return;

        if (BpMessage.Confirm(this, $"Use the recommended pacing for {TransportCatalog.NameOf(p.Transport.Kind)}?",
                              "Pacing", includeCancel: false) == BpConfirmResult.Yes)
            ApplyPacing(p, recommended);
    }

    private void RecommendedPacing_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p) ApplyPacing(p, TransportCatalog.RecommendedPacing(p.Transport.Kind));
    }

    private void ApplyPacing(PrinterProfile p, PacingSettings pacing)
    {
        p.Pacing = pacing;
        if (ReferenceEquals(Selected, p)) ShowSelected();
    }

    // Parts of the form that depend on other values: what is shown, what is enabled.
    private void UpdateDerived()
    {
        if (Selected is not { } p) return;

        var kind = p.Transport.Kind;
        TcpPanel.Visibility = Shown(kind == TransportKind.Tcp);
        UsbPanel.Visibility = Shown(kind == TransportKind.Usb);
        SpoolerPanel.Visibility = Shown(kind == TransportKind.SpoolerRaw);
        SerialPanel.Visibility = Shown(kind == TransportKind.Serial);

        var language = p.PrinterCommand;
        TsplPanel.Visibility = Shown(language == PrinterLanguage.Tspl);
        ZplPanel.Visibility = Shown(language == PrinterLanguage.Zpl);
        EplPanel.Visibility = Shown(language == PrinterLanguage.Epl);
        EzplPanel.Visibility = Shown(language == PrinterLanguage.Ezpl);

        ThresholdText.Text = Format(p.Raster.Threshold);
    }

    // ---- print width: typed in cm, kept in dots --------------------------------

    // Users know the width of the head in cm, not in dots. The dots are worked out
    // from the resolution and rounded to a multiple of 8 (the raster is sent a
    // byte at a time); the box then shows the cm those dots really are.
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

    private void CommitWidth()
    {
        // A width that is not a number stays as typed, with its problem under the form.
        if (Selected is not { Dpi: > 0 } p || !double.TryParse(WidthCmBox.Text.Trim(), NumberStyles.Float, Invariant, out _))
            return;

        _loading = true;
        try { WidthCmBox.Text = Format(p.PrintWidthMm / 10); }
        finally { _loading = false; }
        ShowWidthDots(p);
    }

    private void ShowWidthDots(PrinterProfile p)
        => WidthDotsText.Text = p is { Dpi: > 0, PrintWidthDots: > 0 } ? $"= {Format(p.PrintWidthDots)} dots" : "";

    private void WidthCmBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitWidth();
        e.Handled = true;   // Enter commits the width; it does not press Save
    }

    private void WidthCmBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitWidth();

    // Every profile is checked, not only the one on screen: Save writes them all.
    private void Validate()
    {
        var lines = new List<string>(_formProblems);
        foreach (var p in _profiles)
            foreach (var problem in ProfileValidator.Validate(p, _profiles))
                lines.Add(ReferenceEquals(p, Selected)
                    ? problem.ToString()
                    : $"{(string.IsNullOrWhiteSpace(p.Name) ? "(no name)" : p.Name)} - {problem}");

        Problems.SetLines(BpMessageKind.Error, lines);
        SaveButton.IsEnabled = lines.Count == 0;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Validate();
        if (!SaveButton.IsEnabled) return;

        foreach (var p in _profiles) p.Name = p.Name.Trim();
        Result = [.. _profiles];
        SelectedName = Selected?.Name;
        DialogResult = true;
    }

    // ---- helpers -------------------------------------------------------------

    private int ReadInt(string text, string field, int current)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, Invariant, out var value)) return value;
        _formProblems.Add($"{field}: Enter a whole number.");
        return current;
    }

    private double ReadDouble(string text, string field, double current)
    {
        if (double.TryParse(text.Trim(), NumberStyles.Float, Invariant, out var value)) return value;
        _formProblems.Add($"{field}: Enter a number.");
        return current;
    }

    // Empty means "use the printer's own setting".
    private int? ReadOptionalInt(string text, string field, int? current)
        => string.IsNullOrWhiteSpace(text) ? null : ReadInt(text, field, current ?? 0);

    private double? ReadOptionalDouble(string text, string field, double? current)
        => string.IsNullOrWhiteSpace(text) ? null : ReadDouble(text, field, current ?? 0);

    private static string Format(int value) => value.ToString(Invariant);
    private static string Format(int? value) => value?.ToString(Invariant) ?? "";
    private static string Format(double value) => value.ToString("0.###", Invariant);
    private static string Format(double? value) => value?.ToString("0.###", Invariant) ?? "";

    // The label row is typed in cm, like the designer, and kept in mm in the profile.
    // One more decimal than mm needs, so a value is shown as it is and saving the
    // form unchanged keeps it to the last digit.
    private static string FormatCm(double mm) => (mm / 10).ToString("0.####", Invariant);

    private double ReadCm(string text, string field, double currentMm)
        => Math.Round(ReadDouble(text, field, currentMm / 10) * 10, 3);

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private static Choice[] Choices<T>() where T : struct, Enum
        => Enum.GetValues<T>().Select(v => new Choice(v, v.ToString())).ToArray();

    private static void Select(ComboBox box, object? value)
        => box.SelectedItem = box.Items.OfType<Choice>().FirstOrDefault(c => Equals(c.Value, value));

    private static T Value<T>(ComboBox box, T current) => box.SelectedItem is Choice { Value: T value } ? value : current;

    private static T? OptionalValue<T>(ComboBox box, T? current) where T : struct
        => box.SelectedItem is Choice choice ? (T?)choice.Value : current;
}
