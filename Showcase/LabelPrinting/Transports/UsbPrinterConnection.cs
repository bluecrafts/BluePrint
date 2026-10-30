using System.IO;
using System.Runtime.InteropServices;
using LabelPrinting.Profiles;
using LabelPrinting.Transports.Discovery;
using LabelPrinting.Transports.Native;

namespace LabelPrinting.Transports;

/// <summary>
/// USB direct: writes to the device that Windows' own USB printer driver
/// (usbprint.sys) creates when the printer is plugged in, with CreateFile and an
/// asynchronous FileStream. No printer driver and no print queue are needed.
/// <para>The device is looked up by its instance id every time it is opened:
/// the device path can change when the printer is plugged in again.</para>
/// </summary>
public sealed class UsbPrinterConnection(string? instanceId, string? displayName) : IPrinterConnection
{
    private FileStream? _stream;

    public TransportKind Kind => TransportKind.Usb;
    public string Describe => $"USB {(string.IsNullOrEmpty(displayName) ? instanceId : displayName)}";

    public Task OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new IOException("Choose a USB printer in the printer profile first.");

        var device = UsbPrinterDiscovery.FindByInstanceId(instanceId)
            ?? throw new IOException($"The USB printer \"{(string.IsNullOrEmpty(displayName) ? instanceId : displayName)}\" is not connected.");

        var handle = SetupApi.CreateFile(device.DevicePath, SetupApi.GenericWrite, SetupApi.FileShareRead | SetupApi.FileShareWrite,
                                         IntPtr.Zero, SetupApi.OpenExisting, SetupApi.FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException($"Could not open the USB printer \"{device.Name}\" (error {error}). " +
                                  "If a print queue uses this printer, try the Windows spooler (RAW) connection.");
        }

        _stream = new FileStream(handle, FileAccess.Write, bufferSize: 0, isAsync: true);
        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var stream = _stream ?? throw new InvalidOperationException("The USB printer is not open.");
        try
        {
            await stream.WriteAsync(data, ct);
        }
        catch (IOException ex)
        {
            throw new IOException($"USB write failed ({ex.Message.TrimEnd('.')}). " +
                                  "If a print queue uses this printer, try the Windows spooler (RAW) connection.", ex);
        }
    }

    public Task FlushAsync(CancellationToken ct) => _stream?.FlushAsync(ct) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync();
        _stream = null;
    }
}
