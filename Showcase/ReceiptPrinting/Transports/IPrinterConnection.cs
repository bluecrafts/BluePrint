using ReceiptPrinting.Profiles;

namespace ReceiptPrinting.Transports;

/// <summary>
/// A way to get bytes to the printer. One print job opens a connection once,
/// writes to it as many times as it needs, and always disposes it - after an
/// error or a Stop too - so no handle is left open between jobs.
/// <para>Exceptions are <see cref="System.IO.IOException"/> with a message in
/// English that names the connection, ready to show to the user.</para>
/// </summary>
public interface IPrinterConnection : IAsyncDisposable
{
    TransportKind Kind { get; }

    /// <summary>"TCP 192.0.2.10:9100", "USB XP-420B" - shown with the result of the job.</summary>
    string Describe { get; }

    Task OpenAsync(CancellationToken ct);
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <summary>Everything has been written: finish the job properly when disposed.</summary>
    Task FlushAsync(CancellationToken ct);
}
