using ReceiptPrinting.Profiles;

namespace ReceiptPrinting.Transports;

/// <summary>The connection a printer profile describes.</summary>
public static class PrinterConnectionFactory
{
    /// <param name="jobName">The job's name in the Windows print queue; the other connections do not use it.</param>
    public static IPrinterConnection Create(TransportSettings transport, string jobName = "ReceiptPrinting") => transport.Kind switch
    {
        TransportKind.Tcp => new TcpPrinterConnection(transport.Tcp.Host.Trim(), transport.Tcp.Port, transport.Tcp.ConnectTimeoutMs),
        TransportKind.Usb => new UsbPrinterConnection(transport.Usb.DeviceInstanceId, transport.Usb.DeviceName),
        TransportKind.SpoolerRaw => new SpoolerRawPrinterConnection(transport.SpoolerRaw.QueueName, jobName),
        TransportKind.Serial => new SerialPrinterConnection(transport.Serial),
        _ => throw new NotSupportedException($"Unknown connection kind {transport.Kind}."),
    };

    /// <summary>
    /// Opens the connection and closes it again without sending anything: proves
    /// the printer can be reached. A spooler job that is opened and dropped is
    /// aborted, so nothing reaches the printer either.
    /// </summary>
    public static async Task TestAsync(TransportSettings transport, CancellationToken ct)
    {
        await using var connection = Create(transport, "ReceiptPrinting connection test");
        await connection.OpenAsync(ct);
    }
}
