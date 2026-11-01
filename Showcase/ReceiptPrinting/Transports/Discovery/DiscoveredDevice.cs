namespace ReceiptPrinting.Transports.Discovery;

/// <summary>
/// A device found on this computer. <see cref="Id"/> is what the printer
/// profile keeps: the device instance id of a USB printer, the name of a print
/// queue, the name of a COM port.
/// <para><see cref="ToString"/> is the text in the combo box.</para>
/// </summary>
public sealed record DiscoveredDevice(string Id, string DisplayName, string? Detail = null, bool Connected = true)
{
    public override string ToString() =>
        Connected ? (Detail is null ? DisplayName : $"{DisplayName} ({Detail})") : $"{DisplayName} (not connected)";
}
