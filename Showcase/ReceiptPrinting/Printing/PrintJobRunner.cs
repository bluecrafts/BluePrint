using System.Diagnostics;
using ReceiptPrinting.Profiles;
using ReceiptPrinting.Transports;

namespace ReceiptPrinting.Printing;

/// <summary>
/// Progress after each piece that was sent. A continuous receipt is one page of
/// several megabytes, so the job reports bytes, not pages.
/// </summary>
/// <param name="Page">The page being sent, from 1.</param>
/// <param name="BytesSent">Bytes of the whole job sent so far.</param>
/// <param name="PageBytesSent">Bytes of this page sent so far.</param>
/// <param name="PageBytes">Bytes of this page.</param>
public readonly record struct PrintJobProgress(int Page, int PageCount, long BytesSent, long PageBytesSent, long PageBytes)
{
    /// <summary>How much of the job is done, 0 to 1: whole pages, and the part of the page being sent.</summary>
    public double Fraction => PageCount <= 0 ? 0
        : (Page - 1 + (PageBytes > 0 ? (double)PageBytesSent / PageBytes : 1)) / PageCount;
}

/// <summary>A job that was sent completely.</summary>
public sealed record PrintJobResult(int Pages, long Bytes, TimeSpan Elapsed);

/// <summary>A job stopped by the user, with how much had been sent by then.</summary>
public sealed class PrintJobStoppedException(long bytesSent)
    : OperationCanceledException($"Stopped after {bytesSent:N0} bytes. The printer may still print the data it has received.")
{
    public long BytesSent { get; } = bytesSent;
}

/// <summary>
/// Sends one job: open the connection, send page after page - in pieces, with
/// the pauses the profile's pacing asks for - flush, and always close.
/// <para>Pages are produced lazily: the image of the next page is made only when
/// it is about to be sent, so long jobs do not fill the memory. Stop takes effect
/// between pieces. Failures are reported, never retried.</para>
/// <para>Unlike LabelPrinting, a page is never written in one piece: without a
/// chunk size it still goes in pieces of <see cref="UnpacedPieceBytes"/>, with no
/// pause, so that a receipt of several metres shows its progress and can be stopped.</para>
/// </summary>
public sealed class PrintJobRunner(Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>The piece written at once when the profile sets no chunk size (pacing 0).</summary>
    public const int UnpacedPieceBytes = 64 * 1024;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<PrintJobResult> RunAsync(
        IPrinterConnection connection, IEnumerable<CommandSegment> pages, int pageCount, PacingSettings pacing,
        IProgress<PrintJobProgress>? progress = null, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        long sent = 0;
        var done = 0;

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            foreach (var segment in pages)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = pacing.ChunkBytes > 0 ? pacing.ChunkBytes : UnpacedPieceBytes;
                var pause = pacing.ChunkBytes > 0 ? pacing.InterChunkDelayMs : 0;
                var length = segment.Bytes.Length;
                for (var offset = 0; offset < length; offset += chunk)
                {
                    if (offset > 0 && pause > 0)
                        await _delay(TimeSpan.FromMilliseconds(pause), ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();

                    var piece = Math.Min(chunk, length - offset);
                    await connection.WriteAsync(segment.Bytes.Slice(offset, piece), ct).ConfigureAwait(false);
                    sent += piece;
                    progress?.Report(new PrintJobProgress(done + 1, pageCount, sent, offset + piece, length));
                }
                if (length == 0) progress?.Report(new PrintJobProgress(done + 1, pageCount, sent, 0, 0));

                done++;
                if (segment.Pace && pacing.InterPageDelayMs > 0)
                    await _delay(TimeSpan.FromMilliseconds(pacing.InterPageDelayMs), ct).ConfigureAwait(false);
            }

            await connection.FlushAsync(ct).ConfigureAwait(false);
            return new PrintJobResult(done, sent, watch.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new PrintJobStoppedException(sent);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
