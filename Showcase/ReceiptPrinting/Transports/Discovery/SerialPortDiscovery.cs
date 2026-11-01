using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ReceiptPrinting.Transports.Native;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ReceiptPrinting.Transports.Discovery;

/// <summary>A COM port. <see cref="Description"/> is Windows' name for it without the "(COMn)" at the end.</summary>
public sealed record SerialPortInfo(string PortName, string? Description, bool Bluetooth);

/// <summary>
/// The COM ports of this computer: SerialPort.GetPortNames() for the list, and
/// SetupAPI for the names Windows gives them. A paired Bluetooth printer shows
/// up as a COM port whose device starts with BTHENUM\, and is marked so.
/// </summary>
public static partial class SerialPortDiscovery
{
    public static IReadOnlyList<SerialPortInfo> Find()
    {
        var found = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in FromSetupApi()) found.TryAdd(port.PortName, port);
        // A port SetupAPI does not describe (some virtual port drivers) is still listed, without a name.
        foreach (var name in SerialPort.GetPortNames()) found.TryAdd(name, new SerialPortInfo(name, null, false));
        return [.. found.Values.OrderBy(p => PortNumber(p.PortName)).ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>"COM5 - Standard Serial over Bluetooth link (Bluetooth)".</summary>
    public static DiscoveredDevice ToDevice(SerialPortInfo port) =>
        new(port.PortName,
            port.Description is null ? port.PortName : $"{port.PortName} - {port.Description}",
            port.Bluetooth ? "Bluetooth" : null);

    /// <summary>"USB-SERIAL CH340 (COM3)" becomes "USB-SERIAL CH340".</summary>
    public static string StripPortSuffix(string friendlyName, string portName)
    {
        var suffix = $"({portName})";
        var text = friendlyName.Trim();
        return text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? text[..^suffix.Length].TrimEnd() : text;
    }

    /// <summary>COM2 before COM10; names that are not COMn go last.</summary>
    public static int PortNumber(string portName) =>
        ComNumber().Match(portName) is { Success: true } m ? int.Parse(m.Groups[1].Value) : int.MaxValue;

    [GeneratedRegex(@"^COM(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ComNumber();

    [GeneratedRegex(@"\((COM\d+)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PortInName();

    private static List<SerialPortInfo> FromSetupApi()
    {
        var result = new List<SerialPortInfo>();
        var guid = SetupApi.PortsClass;
        var set = SetupApi.SetupDiGetClassDevs(in guid, IntPtr.Zero, IntPtr.Zero, SetupApi.DigcfPresent);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new SetupApi.SpDevinfoData { CbSize = Marshal.SizeOf<SetupApi.SpDevinfoData>() };
                if (!SetupApi.SetupDiEnumDeviceInfo(set, index, ref info)) break;

                var friendly = SetupApi.Property(set, ref info, SetupApi.SpdrpFriendlyName)
                               ?? SetupApi.Property(set, ref info, SetupApi.SpdrpDeviceDesc);
                var port = PortName(set, ref info)
                           ?? (friendly is not null && PortInName().Match(friendly) is { Success: true } m ? m.Groups[1].Value : null);
                // The same device class holds LPT ports too.
                if (port is null || !port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) continue;

                var instanceId = SetupApi.InstanceId(set, ref info);
                var bluetooth = instanceId?.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) == true;
                result.Add(new SerialPortInfo(port, friendly is null ? null : StripPortSuffix(friendly, port), bluetooth));
            }
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    private static string? PortName(IntPtr set, ref SetupApi.SpDevinfoData info)
    {
        var handle = SetupApi.SetupDiOpenDevRegKey(set, ref info, SetupApi.DicsFlagGlobal, 0, SetupApi.DiregDev, SetupApi.KeyRead);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;
        using var key = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
        return key.GetValue("PortName") as string;
    }
}
