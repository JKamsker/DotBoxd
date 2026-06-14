using System.Buffers.Binary;
using System.IO.Pipes;
using DotBoxd.Services.Buffers;
using DotBoxd.Services.Protocol;

namespace DotBoxd.Services.Transport;

/// <summary>
/// DotBoxd connection over a duplex stream, including named pipe streams.
/// </summary>
public sealed class StreamConnection : IRpcChannel
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _remoteEndpoint;
    private readonly int _maxMessageSize;
    private readonly TimeSpan _frameReadIdleTimeout;
    private readonly byte[] _lengthBuffer = new byte[4];
    private int _disposed;

    /// <summary>
    /// Creates a framed connection over <paramref name="stream"/>. <paramref name="frameReadIdleTimeout"/>
    /// mirrors <c>TcpConnection</c>'s slow-loris defense: it bounds how long an in-progress frame body read
    /// may stall with no data before the connection is torn down (surfaced as an <see cref="IOException"/>).
    /// Pass <see langword="null"/> or <see cref="Timeout.InfiniteTimeSpan"/> to leave body reads untimed
    /// (the default, preserving the original behaviour for clients and direct callers).
    /// </summary>
    public StreamConnection(
        Stream stream,
        string? remoteEndpoint = null,
        bool ownsStream = true,
        int maxMessageSize = MessageFramer.MaxMessageSize,
        TimeSpan? frameReadIdleTimeout = null)
    {
        if (maxMessageSize < MessageFramer.HeaderSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessageSize),
                maxMessageSize,
                "Maximum message size must be at least the DotBoxd header size.");
        }

        var timeout = frameReadIdleTimeout ?? Timeout.InfiniteTimeSpan;
        if (timeout != Timeout.InfiniteTimeSpan &&
            (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameReadIdleTimeout),
                timeout,
                "Frame read idle timeout must be positive (at most int.MaxValue ms) or Timeout.InfiniteTimeSpan.");
        }

        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;
        _remoteEndpoint = remoteEndpoint ?? "stream";
        _maxMessageSize = maxMessageSize;
        _frameReadIdleTimeout = timeout;
    }

    /// <summary>
    /// Configured inter-read idle timeout for in-progress frame body reads
    /// (<see cref="Timeout.InfiniteTimeSpan"/> when disabled). Test/transport seam.
    /// </summary>
    internal TimeSpan FrameReadIdleTimeout => _frameReadIdleTimeout;

    public bool IsConnected =>
        Volatile.Read(ref _disposed) == 0 &&
        (_stream is not PipeStream pipe || pipe.IsConnected);

    public string RemoteEndpoint => _remoteEndpoint;

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        MessageFramer.ValidateOutgoingFrame(data.Span, _maxMessageSize);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            if (_stream is not PipeStream)
            {
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // CloseAsync disposed the send lock while this send was in flight; the real I/O fault
                // (if any) already propagates from the WriteAsync above. Mirrors TcpConnection.SendAsync.
            }
        }
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var read = await ReadExactAsync(_lengthBuffer.AsMemory(0, 4), ct, timeFirstRead: false).ConfigureAwait(false);
        if (read < 4)
        {
            return Payload.Empty;
        }

        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(_lengthBuffer.AsSpan(0, 4));
        ValidateIncomingLength(totalLength);

        var frame = Payload.Rent(totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(frame.Memory.Span.Slice(0, 4), totalLength);

        try
        {
            read = await ReadExactAsync(frame.Memory.Slice(4), ct, timeFirstRead: true).ConfigureAwait(false);
            if (read < totalLength - 4)
            {
                frame.Dispose();
                if (read == 0)
                {
                    return Payload.Empty;
                }

                throw new InvalidDataException(
                    $"Connection closed after {read + 4} of {totalLength} frame bytes.");
            }
        }
        catch
        {
            frame.Dispose();
            throw;
        }

        return frame;
    }

    /// <summary>
    /// Closes the connection. This operation is idempotent.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsStream)
        {
            await DisposeStreamAsync(_stream).ConfigureAwait(false);
        }

        _sendLock.Dispose();
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StreamConnection));
        }
    }

    private void ValidateIncomingLength(int totalLength)
    {
        if (totalLength < MessageFramer.HeaderSize || totalLength > _maxMessageSize)
        {
            throw new InvalidDataException($"Invalid DotBoxd frame length: {totalLength}.");
        }
    }

    private async Task<int> ReadExactAsync(Memory<byte> buffer, CancellationToken ct, bool timeFirstRead)
    {
        var totalRead = 0;
        CancellationTokenSource? timeoutCts = null;
        try
        {
            while (totalRead < buffer.Length)
            {
                // Apply the idle timeout once a frame is in progress: always for body reads, and for the
                // length prefix only after its first byte has arrived (the initial wait is idle, not a stall).
                var applyTimeout = timeFirstRead || totalRead > 0;
                if (applyTimeout &&
                    timeoutCts is null &&
                    _frameReadIdleTimeout != Timeout.InfiniteTimeSpan)
                {
                    timeoutCts = CreateFrameReadTimeoutSource(ct);
                }

                var read = await ReadChunkAsync(buffer.Slice(totalRead), ct, timeoutCts, applyTimeout)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return totalRead;
                }

                totalRead += read;
            }
        }
        finally
        {
            timeoutCts?.Dispose();
        }

        return totalRead;
    }

    private async Task<int> ReadChunkAsync(
        Memory<byte> buffer,
        CancellationToken ct,
        CancellationTokenSource? timeoutCts,
        bool applyTimeout)
    {
        if (!applyTimeout || timeoutCts is null)
        {
            return await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        }

        timeoutCts.CancelAfter(_frameReadIdleTimeout);
        try
        {
            return await _stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new IOException(
                $"Inbound frame read stalled for longer than {_frameReadIdleTimeout} with no data (possible slow-loris peer).");
        }
    }

    private static CancellationTokenSource CreateFrameReadTimeoutSource(CancellationToken ct) =>
        ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();

    private static async ValueTask DisposeStreamAsync(Stream stream)
    {
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Closing is best-effort.
        }
    }
}
