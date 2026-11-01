using System.IO;
using System.Net.Sockets;
using ReceiptPrinting.Profiles;

namespace ReceiptPrinting.Transports;

/// <summary>
/// TCP - a printer on the network, wired or Wi-Fi alike. Receipt printers listen
/// for raw commands on port 9100.
/// </summary>
public sealed class TcpPrinterConnection(string host, int port, int connectTimeoutMs = 3000, int sendTimeoutMs = 10000)
    : IPrinterConnection
{
    private TcpClient? _client;
    private NetworkStream? _stream;

    public TransportKind Kind => TransportKind.Tcp;
    public string Describe => $"TCP {host}:{port}";

    public async Task OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new IOException("Enter the printer's host name or IP address.");

        _client = new TcpClient { NoDelay = true, SendTimeout = sendTimeoutMs };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(connectTimeoutMs);
        try
        {
            await _client.ConnectAsync(host, port, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException($"Could not connect to {host}:{port}. No answer within {connectTimeoutMs} ms.");
        }
        catch (SocketException ex)
        {
            throw new IOException($"Could not connect to {host}:{port}. {ex.Message}", ex);
        }
        _stream = _client.GetStream();
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var stream = _stream ?? throw new InvalidOperationException("The connection is not open.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(sendTimeoutMs);
        try
        {
            await stream.WriteAsync(data, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException($"Sending to {host}:{port} timed out after {sendTimeoutMs} ms. The printer may be busy or out of paper.");
        }
        catch (Exception ex) when (ex is SocketException or IOException { InnerException: SocketException })
        {
            throw new IOException($"Sending to {host}:{port} failed. {ex.Message}", ex);
        }
    }

    public Task FlushAsync(CancellationToken ct) => _stream?.FlushAsync(ct) ?? Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        return ValueTask.CompletedTask;
    }
}
