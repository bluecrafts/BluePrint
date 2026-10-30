using System.Diagnostics;
using LabelPrinting.Profiles;
using LabelPrinting.Transports;

namespace LabelPrinting.Printing;

/// <summary>Progress after each page (each row of labels).</summary>
public readonly record struct PrintJobProgress(int Page, int PageCount, long BytesSent);

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
/// </summary>
public sealed class PrintJobRunner(Func<TimeSpan, CancellationToken, Task>? delay = null)
{
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

                var chunk = pacing.ChunkBytes > 0 ? pacing.ChunkBytes : Math.Max(1, segment.Bytes.Length);
                var pieces = 0;
                for (var offset = 0; offset < segment.Bytes.Length; offset += chunk)
                {
                    if (pieces > 0 && pacing.InterChunkDelayMs > 0)
                        await _delay(TimeSpan.FromMilliseconds(pacing.InterChunkDelayMs), ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();

                    var length = Math.Min(chunk, segment.Bytes.Length - offset);
                    await connection.WriteAsync(segment.Bytes.Slice(offset, length), ct).ConfigureAwait(false);
                    sent += length;
                    pieces++;
                }

                done++;
                progress?.Report(new PrintJobProgress(done, pageCount, sent));

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
