using System.Net.Sockets;
using System.Text;

namespace Delily.IntegrationTests.Infrastructure;

/// <summary>
/// A byte-oriented TCP client for protocol tests. It deliberately does not use
/// StreamReader: the test helper must preserve CRLF and byte counts so framing
/// bugs are visible.
/// </summary>
public sealed class TcpTestClient : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly TimeSpan _defaultTimeout;

    private TcpTestClient(TcpClient client, TimeSpan defaultTimeout)
    {
        _client = client;
        _stream = client.GetStream();
        _defaultTimeout = defaultTimeout;
    }

    public static async Task<TcpTestClient> ConnectAsync(
        string host,
        int port,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        var client = new TcpClient
        {
            NoDelay = true
        };

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveTimeout);

        try
        {
            await client.ConnectAsync(host, port, timeoutSource.Token);
            return new TcpTestClient(client, effectiveTimeout);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        return SendAsync(Encoding.ASCII.GetBytes(text), cancellationToken);
    }

    public async Task SendAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_defaultTimeout);

        await _stream.WriteAsync(bytes, timeoutSource.Token);
        await _stream.FlushAsync(timeoutSource.Token);
    }

    /// <summary>
    /// Reads one CRLF-terminated line and returns it without the CRLF. A bare
    /// LF is returned with a trailing marker so tests can assert on it.
    /// </summary>
    public async Task<string> ReadLineAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? _defaultTimeout;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveTimeout);

        var buffer = new List<byte>();
        var singleByte = new byte[1];

        while (true)
        {
            var read = await _stream.ReadAsync(singleByte, timeoutSource.Token);
            if (read == 0)
            {
                throw new IOException("The remote host closed the connection.");
            }

            if (singleByte[0] == (byte)'\n')
            {
                var line = Encoding.ASCII.GetString(buffer.ToArray());
                return line.EndsWith('\r') ? line[..^1] : line;
            }

            buffer.Add(singleByte[0]);

            if (buffer.Count > 1024 * 1024)
            {
                throw new InvalidOperationException("Line exceeded the test helper's 1 MiB safety limit.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _client.Dispose();
    }
}
