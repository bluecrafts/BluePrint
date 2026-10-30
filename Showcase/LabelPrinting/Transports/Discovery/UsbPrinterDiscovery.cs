using System.Runtime.InteropServices;
using LabelPrinting.Transports.Native;

namespace LabelPrinting.Transports.Discovery;

/// <summary>A USB printer. <see cref="DevicePath"/> opens it (and can change when it is plugged in again); <see cref="InstanceId"/> is kept in the profile.</summary>
public sealed record UsbPrinterDevice(string InstanceId, string Name, string DevicePath);

/// <summary>
/// The USB printers that are plugged in, found through the interface Windows'
/// USB printer driver registers - printers only, not every USB device.
/// </summary>
public static class UsbPrinterDiscovery
{
    public static IReadOnlyList<UsbPrinterDevice> Find()
    {
        var result = new List<UsbPrinterDevice>();
        var guid = SetupApi.UsbPrintInterface;
        var set = SetupApi.SetupDiGetClassDevs(in guid, IntPtr.Zero, IntPtr.Zero, SetupApi.DigcfPresent | SetupApi.DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

        try
        {
            for (uint index = 0; ; index++)
            {
                var iface = new SetupApi.SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SetupApi.SpDeviceInterfaceData>() };
                if (!SetupApi.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, in guid, index, ref iface)) break;

                var info = new SetupApi.SpDevinfoData { CbSize = Marshal.SizeOf<SetupApi.SpDevinfoData>() };
                if (DevicePath(set, ref iface, ref info) is not { } path) continue;

                var instanceId = SetupApi.InstanceId(set, ref info) ?? path;
                var name = SetupApi.Property(set, ref info, SetupApi.SpdrpFriendlyName)
                           ?? SetupApi.Property(set, ref info, SetupApi.SpdrpDeviceDesc)
                           ?? instanceId;
                result.Add(new UsbPrinterDevice(instanceId, name, path));
            }
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    /// <summary>The printer with the instance id a profile keeps; null when it is not plugged in.</summary>
    public static UsbPrinterDevice? FindByInstanceId(string? instanceId) =>
        instanceId is null ? null : Find().FirstOrDefault(d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

    public static DiscoveredDevice ToDevice(UsbPrinterDevice device) => new(device.InstanceId, device.Name);

    private static string? DevicePath(IntPtr set, ref SetupApi.SpDeviceInterfaceData iface, ref SetupApi.SpDevinfoData info)
    {
        SetupApi.SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out var required, ref info);
        if (required <= 0) return null;

        var buffer = Marshal.AllocHGlobal(required);
        try
        {
            Marshal.WriteInt32(buffer, SetupApi.DetailCbSize);
            if (!SetupApi.SetupDiGetDeviceInterfaceDetail(set, ref iface, buffer, required, out _, ref info)) return null;
            return Marshal.PtrToStringUni(buffer + SetupApi.DetailPathOffset);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
