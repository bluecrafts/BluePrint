// A printer profile: everything needed to turn a receipt into ESC/POS bytes for
// one printer and send them there. Profiles are saved in ReceiptPrinting.cfg (see
// AppConfigStore), as JSON with camelCase names.
//
// Receipt printers all speak ESC/POS, so unlike LabelPrinting there is no printer
// command to choose, and there is no label size: the paper length comes from the
// data.

using System.Text.Json.Serialization;
using BluePrint.Render.Export.Mono;
using BluePrint.Render.Export.Mono.Printers;

namespace ReceiptPrinting.Profiles;

public sealed class PrinterProfile
{
    /// <summary>Shown in the profile lists; unique, case-insensitively.</summary>
    public string Name { get; set; } = "";

    /// <summary>Dots per inch of the print head (180, 203...).</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>Width of the print head in dots. A multiple of 8.</summary>
    public int PrintWidthDots { get; set; } = 576;

    /// <summary>
    /// Dots per inch down the paper, when the head's dots are not square (a dot
    /// matrix printer: the Epson TM-U220 is 80 across and 72 down). Null: the same
    /// as <see cref="Dpi"/>.
    /// </summary>
    public int? VerticalDpi { get; set; }

    public RasterSettings Raster { get; set; } = new();
    public ReceiptCommands Commands { get; set; } = new();
    public TransportSettings Transport { get; set; } = new();
    public PacingSettings Pacing { get; set; } = new();

    /// <summary>The print width in millimetres, for display.</summary>
    [JsonIgnore]
    public double PrintWidthMm => Dpi > 0 ? PrintWidthDots * 25.4 / Dpi : 0;

    /// <summary>
    /// Takes every value of <paramref name="other"/>, keeping this object, so that
    /// the lists and combo boxes that show it stay on it.
    /// </summary>
    /// <remarks>
    /// <paramref name="other"/> must be a copy made for this (AppConfigStore.Clone):
    /// its parts are taken as they are, not copied again.
    /// </remarks>
    public void CopyFrom(PrinterProfile other)
    {
        Name = other.Name;
        Dpi = other.Dpi;
        PrintWidthDots = other.PrintWidthDots;
        VerticalDpi = other.VerticalDpi;
        Raster = other.Raster;
        Commands = other.Commands;
        Transport = other.Transport;
        Pacing = other.Pacing;
    }
}

/// <summary>How wide the 1-bit image is made.</summary>
public enum ImageWidthMode
{
    /// <summary>As wide as the form, at the printer's resolution: the receipt prints at its actual size.</summary>
    Report,

    /// <summary>As wide as the print head: the whole receipt is scaled to fill it.</summary>
    PrintHead,
}

/// <summary>How the receipt is turned into black and white dots.</summary>
public sealed class RasterSettings
{
    /// <summary>
    /// Report (the default) prints a 58 mm form at its size on an 80 mm printer, at
    /// the left edge. PrintHead stretches it to the full width - text gets larger.
    /// </summary>
    public ImageWidthMode ImageWidth { get; set; } = ImageWidthMode.Report;

    /// <summary>A grey darker than this prints black (0-255).</summary>
    public int Threshold { get; set; } = 176;

    public MonoDitherScope DitherScope { get; set; } = MonoDitherScope.Images;
    public MonoDitherAlgorithm DitherAlgorithm { get; set; } = MonoDitherAlgorithm.Atkinson;
}

/// <summary>
/// The options of BluePrint's command encoder that a receipt printer uses: the
/// ones shared by every language, and ESC/POS. The ESC/POS part is BluePrint's own
/// class, so every name in the file is the one the encoder uses.
/// </summary>
public sealed class ReceiptCommands
{
    /// <summary>Send ESC @ first: the printer starts the job from its own settings.</summary>
    public bool Initialize { get; set; } = true;

    /// <summary>How many times the receipt is printed. The Print panel can change it for one job.</summary>
    public int Copies { get; set; } = 1;

    public EscPosOptions EscPos { get; set; } = new();

    /// <summary>
    /// The encoder's options, for ESC/POS. They share <see cref="EscPos"/> with this
    /// object: a print job works on a copy of the profile (AppConfigStore.Clone).
    /// </summary>
    public PrinterCommandOptions ToOptions() => new() { Initialize = Initialize, Copies = Copies, EscPos = EscPos };
}

public enum TransportKind
{
    Tcp,
    Usb,
    SpoolerRaw,
    Serial,
}

/// <summary>
/// How to reach the printer. The settings of every kind are kept, so switching
/// the kind and back loses nothing.
/// </summary>
public sealed class TransportSettings
{
    public TransportKind Kind { get; set; } = TransportKind.Usb;
    public TcpSettings Tcp { get; set; } = new();
    public UsbSettings Usb { get; set; } = new();
    public SpoolerRawSettings SpoolerRaw { get; set; } = new();
    public SerialSettings Serial { get; set; } = new();
}

public sealed class TcpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 9100;
    public int ConnectTimeoutMs { get; set; } = 3000;
}

public sealed class UsbSettings
{
    /// <summary>Windows' device instance id - stays the same while the printer is unplugged.</summary>
    public string DeviceInstanceId { get; set; } = "";

    /// <summary>The name Windows showed, kept to name the device while it is not connected.</summary>
    public string DeviceName { get; set; } = "";
}

public sealed class SpoolerRawSettings
{
    public string QueueName { get; set; } = "";
}

public sealed class SerialSettings
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public SerialParity Parity { get; set; } = SerialParity.None;
    public int DataBits { get; set; } = 8;
    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;
    public SerialHandshake Handshake { get; set; } = SerialHandshake.RequestToSend;
}

// The application's own copies of the System.IO.Ports enums, so a profile can be
// read and written without the serial port package.
public enum SerialParity { None, Odd, Even, Mark, Space }
public enum SerialStopBits { One, OnePointFive, Two }
public enum SerialHandshake { None, XOnXOff, RequestToSend, RequestToSendXOnXOff }

/// <summary>
/// Pacing: sending the data in pieces with pauses in between, for printers whose
/// input buffer is small and that cannot tell the computer to wait.
/// </summary>
public sealed class PacingSettings
{
    /// <summary>Largest piece sent at once; 0 sends each page in one piece.</summary>
    public int ChunkBytes { get; set; }

    /// <summary>Pause between two pieces of the same page, in milliseconds.</summary>
    public int InterChunkDelayMs { get; set; }

    /// <summary>Pause after each page except the last, in milliseconds.</summary>
    public int InterPageDelayMs { get; set; }
}
