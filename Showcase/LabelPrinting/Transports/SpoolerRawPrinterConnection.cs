using System.IO;
using System.Runtime.InteropServices;
using LabelPrinting.Profiles;
using LabelPrinting.Transports.Native;

namespace LabelPrinting.Transports;

/// <summary>
/// The Windows print queue, with the data type RAW: OpenPrinter, StartDocPrinter
/// ("RAW"), WritePrinter as often as needed, EndDocPrinter.
/// <para>This is not printing through the driver. RAW tells the spooler that the
/// bytes are already in the printer's language, so it passes them to the port
/// unchanged. The queue still needs a driver, but any driver that accepts RAW
/// will do - "Generic / Text Only", which comes with Windows, among them.</para>
/// <para>A job that does not finish (Stop, or an error before FlushAsync) is
/// aborted when disposed, so half a job never reaches the printer.</para>
/// </summary>
public sealed class SpoolerRawPrinterConnection(string? queueName, string jobName) : IPrinterConnection
{
    private SafePrinterHandle? _printer;
    private bool _docStarted;
    private bool _completed;

    public TransportKind Kind => TransportKind.SpoolerRaw;
    public string Describe => $"Windows spooler {queueName}";

    public Task OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(queueName)) throw new IOException("Choose a print queue in the printer profile first.");

        if (!WinSpool.OpenPrinter(queueName, out var printer, IntPtr.Zero))
        {
            var error = Marshal.GetLastPInvokeError();
            printer.Dispose();
            throw new IOException($"Could not open the print queue \"{queueName}\" (error {error}).");
        }
        _printer = printer;

        var docName = Marshal.StringToHGlobalUni(jobName);
        var datatype = Marshal.StringToHGlobalUni("RAW");
        try
        {
            var info = new WinSpool.DocInfo1 { DocName = docName, OutputFile = IntPtr.Zero, Datatype = datatype };
            if (WinSpool.StartDocPrinter(printer, 1, ref info) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new IOException($"The printer driver of \"{queueName}\" does not accept RAW data (error {error}). " +
                                      "Use the USB or TCP connection, or install a Generic / Text Only driver for this queue.");
            }
            _docStarted = true;
        }
        finally
        {
            Marshal.FreeHGlobal(docName);
            Marshal.FreeHGlobal(datatype);
        }

        if (!WinSpool.StartPagePrinter(printer))
            throw new IOException($"Could not start the print job on \"{queueName}\" (error {Marshal.GetLastPInvokeError()}).");
        return Task.CompletedTask;
    }

    public unsafe Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var printer = _printer ?? throw new InvalidOperationException("The print queue is not open.");
        ct.ThrowIfCancellationRequested();

        using var pin = data.Pin();
        if (!WinSpool.WritePrinter(printer, (byte*)pin.Pointer, data.Length, out var written))
            throw new IOException($"Writing to the print queue \"{queueName}\" failed (error {Marshal.GetLastPInvokeError()}).");
        if (written != data.Length)
            throw new IOException($"The print queue \"{queueName}\" accepted {written} of {data.Length} bytes.");
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct)
    {
        _completed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_printer is { } printer)
        {
            if (_docStarted)
            {
                if (_completed)
                {
                    WinSpool.EndPagePrinter(printer);
                    WinSpool.EndDocPrinter(printer);
                }
                else
                {
                    WinSpool.AbortPrinter(printer);
                }
            }
            printer.Dispose();
        }
        _printer = null;
        return ValueTask.CompletedTask;
    }
}
