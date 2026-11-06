using System.IO;
using System.IO.Ports;
using ReceiptPrinting.Profiles;

namespace ReceiptPrinting.Transports;

/// <summary>
/// A serial (COM) port - and a Bluetooth printer, which Windows shows as a COM
/// port once it is paired.
/// <para>A thermal printer on a serial line needs flow control: without it, data
/// that does not fit the printer's buffer is lost without a word. If the printer
/// has none, use pacing (small chunks with a pause) instead.</para>
/// </summary>
public sealed class SerialPrinterConnection(SerialSettings settings, int writeTimeoutMs = 10000) : IPrinterConnection
{
    private SerialPort? _port;

    public TransportKind Kind => TransportKind.Serial;
    public string Describe => $"Serial {settings.PortName} {settings.BaudRate} {Format(settings)}";

    /// <summary>"8N1 RTS/CTS" - the short form printer manuals use.</summary>
    public static string Format(SerialSettings s) =>
        $"{s.DataBits}{ParityLetter(s.Parity)}{StopText(s.StopBits)} {HandshakeName(s.Handshake)}";

    public static string HandshakeName(SerialHandshake h) => h switch
    {
        SerialHandshake.None => "no flow control",
        SerialHandshake.XOnXOff => "XON/XOFF",
        SerialHandshake.RequestToSend => "RTS/CTS",
        SerialHandshake.RequestToSendXOnXOff => "RTS/CTS + XON/XOFF",
        _ => h.ToString(),
    };

    private static char ParityLetter(SerialParity p) => p switch
    {
        SerialParity.Odd => 'O', SerialParity.Even => 'E', SerialParity.Mark => 'M', SerialParity.Space => 'S', _ => 'N',
    };

    private static string StopText(SerialStopBits s) => s switch
    {
        SerialStopBits.OnePointFive => "1.5", SerialStopBits.Two => "2", _ => "1",
    };

    public async Task OpenAsync(CancellationToken ct)
    {
        var name = settings.PortName.Trim();
        if (name.Length == 0) throw new IOException("Choose a COM port in the printer profile first.");

        var port = new SerialPort(name, settings.BaudRate, ToPorts(settings.Parity), settings.DataBits, ToPorts(settings.StopBits))
        {
            Handshake = ToPorts(settings.Handshake),
            WriteTimeout = writeTimeoutMs,
        };
        try
        {
            // A Bluetooth port connects to the printer only now, which can take
            // seconds: keep it off the UI thread.
            await Task.Run(port.Open, ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            port.Dispose();
            throw new IOException($"Could not open {name}. It may be in use by another program.", ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            port.Dispose();
            throw new IOException($"Could not open {name}. The port does not exist or the device is not connected. ({ex.Message.TrimEnd('.')}.)", ex);
        }
        catch
        {
            port.Dispose();
            throw;
        }
        _port = port;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var port = _port ?? throw new InvalidOperationException("The COM port is not open.");
        try
        {
            // .NET's serial stream checks the token only when the write starts, so
            // a printer that holds CTS low would keep it waiting forever. The
            // timeout is enforced here; disposing the port then cancels the write.
            // It is for a printer that stops taking data, not for the whole piece:
            // the piece goes about a second of the line at a time, each part within
            // the timeout - 64 KB at 9600 baud takes over a minute.
            var slice = SliceBytes(settings.BaudRate);
            for (var offset = 0; offset < data.Length; offset += slice)
            {
                var part = data.Slice(offset, Math.Min(slice, data.Length - offset));
                await port.BaseStream.WriteAsync(part, ct).AsTask().WaitAsync(TimeSpan.FromMilliseconds(writeTimeoutMs), ct);
            }
        }
        catch (TimeoutException ex) when (!ct.IsCancellationRequested)
        {
            throw new IOException(TimeoutMessage(port), ex);
        }
        catch (IOException ex)
        {
            throw new IOException($"Sending to {port.PortName} failed. {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Waits until the driver has sent everything: closing earlier could cut off the end of the job.
    /// The timeout starts again whenever the printer takes more data.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        var port = _port ?? throw new InvalidOperationException("The COM port is not open.");
        var deadline = DateTime.UtcNow.AddMilliseconds(writeTimeoutMs);
        var left = port.BytesToWrite;
        while (left > 0)
        {
            if (DateTime.UtcNow > deadline) throw new IOException(TimeoutMessage(port));
            await Task.Delay(20, ct);
            var now = port.BytesToWrite;
            if (now < left) deadline = DateTime.UtcNow.AddMilliseconds(writeTimeoutMs);
            left = now;
        }
    }

    /// <summary>About one second of the line (a byte is 10 bits on the wire), at least 64 bytes.</summary>
    public static int SliceBytes(int baudRate) => Math.Max(64, baudRate / 10);

    private string TimeoutMessage(SerialPort port) =>
        $"Sending to {port.PortName} timed out: the printer took no data for {writeTimeoutMs} ms. Check that the printer is on and has paper, " +
        $"that flow control ({HandshakeName(settings.Handshake)}) matches the printer, or send smaller chunks with a longer delay.";

    public ValueTask DisposeAsync()
    {
        _port?.Dispose();
        _port = null;
        return ValueTask.CompletedTask;
    }

    private static Parity ToPorts(SerialParity p) => p switch
    {
        SerialParity.Odd => Parity.Odd, SerialParity.Even => Parity.Even,
        SerialParity.Mark => Parity.Mark, SerialParity.Space => Parity.Space, _ => Parity.None,
    };

    private static StopBits ToPorts(SerialStopBits s) => s switch
    {
        SerialStopBits.OnePointFive => StopBits.OnePointFive, SerialStopBits.Two => StopBits.Two, _ => StopBits.One,
    };

    private static Handshake ToPorts(SerialHandshake h) => h switch
    {
        SerialHandshake.XOnXOff => Handshake.XOnXOff,
        SerialHandshake.RequestToSend => Handshake.RequestToSend,
        SerialHandshake.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
        _ => Handshake.None,
    };
}
