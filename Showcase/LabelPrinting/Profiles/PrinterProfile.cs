// A printer profile: everything needed to turn a label into bytes for one
// printer and send them there. Profiles are saved in LabelPrinting.cfg (see
// AppConfigStore), as JSON with camelCase names.

using System.Text.Json.Serialization;
using BluePrint.Render.Export.Mono;
using BluePrint.Render.Export.Mono.Printers;

namespace LabelPrinting.Profiles;

public sealed class PrinterProfile
{
    /// <summary>Shown in the profile list; unique, case-insensitively.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The command language the printer understands. This is BluePrint's own
    /// <see cref="PrinterLanguage"/>: the bytes come from PrinterCommands.Encode,
    /// never from this application.
    /// </summary>
    public PrinterLanguage PrinterCommand { get; set; } = PrinterLanguage.Tspl;

    /// <summary>Dots per inch of the print head (203, 300...).</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>Width of the print head in dots. A multiple of 8.</summary>
    public int PrintWidthDots { get; set; } = 832;

    public LabelSettings Label { get; set; } = new();
    public RasterSettings Raster { get; set; } = new();

    /// <summary>
    /// The options of the command encoder, stored as BluePrint's own class so
    /// every name is the one the encoder uses.
    /// </summary>
    public PrinterCommandOptions Commands { get; set; } = new() { Initialize = true };

    public TransportSettings Transport { get; set; } = new();
    public PacingSettings Pacing { get; set; } = new();

    /// <summary>The print width in millimetres, for display.</summary>
    [JsonIgnore]
    public double PrintWidthMm => Dpi > 0 ? PrintWidthDots * 25.4 / Dpi : 0;
}

/// <summary>The label roll: one row of labels across it.</summary>
public sealed class LabelSettings
{
    /// <summary>Width of a whole row of labels - every label across the roll - in mm.</summary>
    public double WidthMm { get; set; } = 100;

    /// <summary>Height of one row of labels, in mm.</summary>
    public double HeightMm { get; set; } = 50;
}

/// <summary>How the label is turned into black and white dots.</summary>
public sealed class RasterSettings
{
    /// <summary>A grey darker than this prints black (0-255).</summary>
    public int Threshold { get; set; } = 176;

    public MonoDitherScope DitherScope { get; set; } = MonoDitherScope.Images;
    public MonoDitherAlgorithm DitherAlgorithm { get; set; } = MonoDitherAlgorithm.Atkinson;

    /// <summary>
    /// How the label, as designed, is turned to go through the printer. Landscape
    /// prints a long label across a narrow print head.
    /// </summary>
    public MonoOrientation Orientation { get; set; } = MonoOrientation.Portrait;

    /// <summary>Print left and right swapped, to read the label through the back of clear film.</summary>
    public bool Mirror { get; set; }

    /// <summary>Print white on black: every dot of the label is swapped.</summary>
    public bool Negative { get; set; }
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
    public TransportKind Kind { get; set; } = TransportKind.Tcp;
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
    /// <summary>Largest piece sent at once; 0 sends each row of labels in one piece.</summary>
    public int ChunkBytes { get; set; }

    /// <summary>Pause between two pieces of the same row, in milliseconds.</summary>
    public int InterChunkDelayMs { get; set; }

    /// <summary>Pause after each row of labels except the last, in milliseconds.</summary>
    public int InterPageDelayMs { get; set; }
}
