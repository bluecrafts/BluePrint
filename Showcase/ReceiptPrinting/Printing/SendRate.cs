using System.Globalization;

namespace ReceiptPrinting.Printing;

/// <summary>
/// How fast a job is going, from the bytes sent in the last few seconds, and
/// what that means for the time left. A serial port sends a long receipt for
/// minutes; the time left is what the user wants to know.
/// </summary>
public sealed class SendRate(TimeSpan? window = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(3);
    private readonly Queue<(TimeSpan At, long Bytes)> _samples = new();

    /// <summary>Records that <paramref name="bytesSent"/> bytes of the job had been sent at <paramref name="at"/>.</summary>
    public void Add(TimeSpan at, long bytesSent)
    {
        _samples.Enqueue((at, bytesSent));
        while (_samples.Count > 2 && at - _samples.Peek().At > _window) _samples.Dequeue();
    }

    /// <summary>Bytes per second over the window; null until there is enough to tell.</summary>
    public double? BytesPerSecond
    {
        get
        {
            if (_samples.Count < 2) return null;
            var (firstAt, firstBytes) = _samples.Peek();
            var (lastAt, lastBytes) = _samples.Last();
            var seconds = (lastAt - firstAt).TotalSeconds;
            return seconds >= 0.25 && lastBytes > firstBytes ? (lastBytes - firstBytes) / seconds : null;
        }
    }

    /// <summary>The time the remaining bytes take at the current rate; null while it is not known.</summary>
    public TimeSpan? TimeLeft(long remainingBytes)
        => BytesPerSecond is { } rate ? TimeSpan.FromSeconds(Math.Max(0, remainingBytes) / rate) : null;

    /// <summary>"12 s", "3 min", "1 h 5 min".</summary>
    public static string Describe(TimeSpan time)
    {
        if (time.TotalSeconds < 60) return $"{Math.Max(1, (int)Math.Ceiling(time.TotalSeconds))} s";
        if (time.TotalMinutes < 60) return $"{(int)Math.Ceiling(time.TotalMinutes)} min";
        return $"{(int)time.TotalHours} h {time.Minutes} min";
    }

    /// <summary>"640 bytes", "12.5 KB", "5.83 MB".</summary>
    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes",
        < 1024 * 1024 => $"{(bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} KB",
        _ => $"{(bytes / (1024.0 * 1024)).ToString("0.00", CultureInfo.InvariantCulture)} MB",
    };
}
