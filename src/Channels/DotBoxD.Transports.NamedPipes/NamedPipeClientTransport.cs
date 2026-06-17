using System.IO.Pipes;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.NamedPipes;

/// <summary>
/// Client transport for connecting to a DotBoxD server over a named pipe.
/// </summary>
public sealed class NamedPipeClientTransport : ITransport
{
    /// <summary>
    /// Default inter-read idle timeout applied to client connections' in-progress frame body reads.
    /// Mirrors <see cref="NamedPipeServerTransport.DefaultFrameReadIdleTimeout"/>.
    /// </summary>
    public static readonly TimeSpan DefaultFrameReadIdleTimeout = NamedPipeServerTransport.DefaultFrameReadIdleTimeout;

    private readonly string _serverName;
    private readonly string _pipeName;
    private readonly int _maxMessageSize;
    private NamedPipeClientStream? _stream;
    private StreamConnection? _connection;
    private CancellationTokenSource? _connectCts;
    private int _disposed;

    public NamedPipeClientTransport(string pipeName, int maxMessageSize = MessageFramer.MaxMessageSize)
        : this(".", pipeName, maxMessageSize)
    {
    }

    public NamedPipeClientTransport(
        string serverName,
        string pipeName,
        int maxMessageSize = MessageFramer.MaxMessageSize)
    {
        _serverName = ValidateName(serverName, nameof(serverName));
        _pipeName = ValidateName(pipeName, nameof(pipeName));
        _maxMessageSize = ValidateMaxMessageSize(maxMessageSize);
    }

    /// <summary>
    /// Inter-read idle timeout applied to the client connection's in-progress frame body reads.
    /// <see langword="null"/> uses <see cref="DefaultFrameReadIdleTimeout"/>;
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables it. See <see cref="StreamConnection"/>.
    /// </summary>
    public TimeSpan? FrameReadIdleTimeout { get; init; }

    public IRpcChannel? Connection => _connection;

    public bool IsConnected => _connection?.IsConnected ?? false;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_connection is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        var stream = new NamedPipeClientStream(
            _serverName,
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stream = stream;
        _connectCts = connectCts;
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(NamedPipeClientTransport));
            }

            await stream.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            _connection = new StreamConnection(
                stream,
                RemoteEndpoint,
                ownsStream: true,
                _maxMessageSize,
                FrameReadIdleTimeout ?? DefaultFrameReadIdleTimeout);
        }
        catch
        {
            if (ReferenceEquals(_stream, stream))
            {
                _stream = null;
            }

            stream.Dispose();
            throw;
        }
        finally
        {
            if (ReferenceEquals(_connectCts, connectCts))
            {
                _connectCts = null;
            }

            connectCts.Dispose();
        }

        // Test seam (null/no-op in production): lets a test deterministically interleave DisposeAsync
        // between the _connection publication above and the disposed re-check below.
        var publishedHook = _onConnectionPublishedForTest;
        if (publishedHook is not null)
        {
            await publishedHook().ConfigureAwait(false);
        }

        // Full store-load fence so the _stream/_connection publication above is globally visible before
        // _disposed is read. Without it an x86/x64 store-buffer (Dekker) interleaving could let this read
        // miss a concurrent DisposeAsync while that DisposeAsync misses _connection, leaking the pipe.
        Interlocked.MemoryBarrier();

        // Close the window where DisposeAsync ran during the connect and observed null fields: tear
        // down the connection we just published so it (and its owned stream) cannot outlive a disposed
        // transport. Mirrors TcpTransport.ConnectAsync.
        if (Volatile.Read(ref _disposed) != 0)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(NamedPipeClientTransport));
        }
    }

    /// <summary>
    /// Test-only seam invoked once after <see cref="_connection"/> is published and before the disposed
    /// re-check, so a test can deterministically interleave <see cref="DisposeAsync"/> there. Never set
    /// in production.
    /// </summary>
    internal Func<Task>? _onConnectionPublishedForTest;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _connectCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ConnectAsync can finish and dispose the linked CTS while DisposeAsync is starting.
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _stream?.Dispose();
        }
    }

    private string RemoteEndpoint => $"pipe://{_serverName}/{_pipeName}";

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(NamedPipeClientTransport));
        }
    }

    private static string ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", parameterName);
        }

        return value;
    }

    private static int ValidateMaxMessageSize(int maxMessageSize)
    {
        if (maxMessageSize < MessageFramer.HeaderSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessageSize),
                maxMessageSize,
                "Maximum message size must be at least the DotBoxD header size.");
        }

        return maxMessageSize;
    }
}
