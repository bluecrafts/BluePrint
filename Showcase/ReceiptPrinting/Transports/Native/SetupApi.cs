using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ReceiptPrinting.Transports.Native;

/// <summary>
/// SetupAPI and CreateFile: finding the USB printers and COM ports Windows
/// knows about, and opening a USB printer for writing.
/// </summary>
internal static partial class SetupApi
{
    /// <summary>
    /// GUID_DEVINTERFACE_USBPRINT, the interface usbprint.sys registers - so the
    /// list holds printers only, not every USB device.
    /// </summary>
    internal static readonly Guid UsbPrintInterface = new("28D78FAD-5A12-11D1-AE5B-0000F803A8C2");

    internal const uint DigcfPresent = 0x02;
    internal const uint DigcfDeviceInterface = 0x10;
    internal const uint SpdrpDeviceDesc = 0x00;
    internal const uint SpdrpFriendlyName = 0x0C;

    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 0x01;
    internal const uint FileShareWrite = 0x02;
    internal const uint OpenExisting = 3;
    internal const uint FileFlagOverlapped = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDevinfoData
    {
        public int CbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    /// <summary>
    /// SP_DEVICE_INTERFACE_DETAIL_DATA_W is a DWORD cbSize followed by the path,
    /// so the path always starts at offset 4. The value put in cbSize is the size
    /// of the struct as the API defines it (8 on 64-bit, 6 on 32-bit), which is
    /// not the offset - mixing the two up loses the start of the path.
    /// </summary>
    internal const int DetailPathOffset = 4;
    internal static int DetailCbSize => IntPtr.Size == 8 ? 8 : 6;

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    internal static partial IntPtr SetupDiGetClassDevs(in Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, in Guid interfaceClassGuid,
                                                             uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
                                                                 IntPtr detail, int detailSize, out int requiredSize,
                                                                 ref SpDevinfoData deviceInfoData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
                                                                   char* instanceId, int size, out int requiredSize);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
                                                                         uint property, out uint propertyRegDataType,
                                                                         byte* buffer, int bufferSize, out int requiredSize);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // ---- COM ports -----------------------------------------------------------

    /// <summary>GUID_DEVCLASS_PORTS: COM and LPT ports, including the virtual ones of Bluetooth and USB-serial adapters.</summary>
    internal static readonly Guid PortsClass = new("4D36E978-E325-11CE-BFC1-08002BE10318");

    internal const uint DicsFlagGlobal = 0x01;
    internal const uint DiregDev = 0x01;
    internal const int KeyRead = 0x20019;

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

    /// <summary>The device's registry key, where PortName ("COM5") is kept. INVALID_HANDLE_VALUE when there is none.</summary>
    [LibraryImport("setupapi.dll", SetLastError = true)]
    internal static partial IntPtr SetupDiOpenDevRegKey(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
                                                        uint scope, uint hwProfile, uint keyType, int samDesired);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
                                                      uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    // ---- helpers shared by the USB and COM port lookups ----------------------

    internal static unsafe string? InstanceId(IntPtr set, ref SpDevinfoData info)
    {
        char* buffer = stackalloc char[512];
        return SetupDiGetDeviceInstanceId(set, ref info, buffer, 512, out _) ? new string(buffer) : null;
    }

    internal static unsafe string? Property(IntPtr set, ref SpDevinfoData info, uint property)
    {
        byte* buffer = stackalloc byte[1024];
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out _, buffer, 1024, out var size) || size < 2)
            return null;
        var text = new string((char*)buffer, 0, size / 2).TrimEnd('\0');
        return text.Length == 0 ? null : text;
    }
}
