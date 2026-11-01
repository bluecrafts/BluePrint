using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ReceiptPrinting.Transports.Native;

/// <summary>A printer handle from OpenPrinterW, closed with ClosePrinter.</summary>
internal sealed class SafePrinterHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePrinterHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle() => WinSpool.ClosePrinter(handle);
}

/// <summary>winspool: sending bytes through the Windows spooler as RAW data.</summary>
internal static partial class WinSpool
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DocInfo1
    {
        public IntPtr DocName;      // LPWSTR
        public IntPtr OutputFile;   // LPWSTR - null sends the job to the queue's port
        public IntPtr Datatype;     // LPWSTR - "RAW"
    }

    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenPrinter(string printerName, out SafePrinterHandle printer, IntPtr defaults);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClosePrinter(IntPtr printer);

    [LibraryImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true)]
    internal static partial int StartDocPrinter(SafePrinterHandle printer, int level, ref DocInfo1 docInfo);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool StartPagePrinter(SafePrinterHandle printer);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool WritePrinter(SafePrinterHandle printer, byte* buffer, int count, out int written);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPagePrinter(SafePrinterHandle printer);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndDocPrinter(SafePrinterHandle printer);

    /// <summary>Deletes the job being spooled - after a Stop or an error, so half a job never prints.</summary>
    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AbortPrinter(SafePrinterHandle printer);
}
