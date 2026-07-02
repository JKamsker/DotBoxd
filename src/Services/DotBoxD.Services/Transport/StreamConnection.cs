using System.Buffers.Binary;
using System.IO.Pipes;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

/// <summary>
/// DotBoxD connection over a duplex stream, including named pipe streams.
/// </summary>
public sealed class StreamConnection : IRpcFrameChannel
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly string _remoteEndpoint;
    private readonly int _maxMessageSize;
    private readonly TimeSpan _frameReadIdleTimeout;
    private readonly FrameReadTimeoutSource? _frameReadTimeout;
    private readonly byte[] _lengthBuffer = new byte[4];
    private int _activeReceives;
    private int _disposed;

    /// <summary>
    /// Creates a framed connection over <paramref name="stream"/>. A null
    /// <paramref name="frameReadIdleTimeout"/> uses the finite default frame-read idle timeout.
    /// Pass <see cref="Timeout.InfiniteTimeSpan"/> to disable the timeout for trusted streams.
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
                "Maximum message size must be at least the DotBoxD header size.");
        }

        var timeout = FrameReadTimeoutSource.Resolve(frameReadIdleTimeout, nameof(frameReadIdleTimeout));

        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;
        _remoteEndpoint = remoteEndpoint ?? "stream";
        _maxMessageSize = maxMessageSize;
        _frameReadIdleTimeout = timeout;
        _frameReadTimeout = timeout == Timeout.InfiniteTimeSpan ? null : new FrameReadTimeoutSource();
    }

    /// <summary>Configured idle timeout for frame reads.</summary>
    internal TimeSpan FrameReadIdleTimeout => _frameReadIdleTimeout;

    public bool IsConnected =>
        Volatile.Read(ref _disposed) == 0 &&
        (_stream is not PipeStream pipe || pipe.IsConnected);

    public string RemoteEndpoint => _remoteEndpoint;

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        SendValueAsync(data, ct).AsTask();

    public async ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        MessageFramer.ValidateOutgoingFrame(data.Span, _maxMessageSize);

        await WaitForSendSlotAsync(ct).ConfigureAwait(false);
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

    public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
        ReceiveValueAsync(ct).AsTask();

    public async ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default)
    {
        ReceiveConcurrencyGuard.Enter(ref _activeReceives, nameof(StreamConnection));

        try
        {
            ThrowIfDisposed();

            var read = await ReadExactAsync(_lengthBuffer.AsMemory(0, 4), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return Payload.Empty;
            }

            if (read < 4)
            {
                throw new InvalidDataException($"Connection closed after {read} of 4 frame length bytes.");
            }

            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(_lengthBuffer.AsSpan(0, 4));
            ValidateIncomingLength(totalLength);

            var frame = Payload.Rent(totalLength);
            BinaryPrimitives.WriteInt32LittleEndian(frame.Memory.Span.Slice(0, 4), totalLength);

            try
            {
                read = await ReadExactAsync(frame.Memory.Slice(4), ct).ConfigureAwait(false);
                if (read < totalLength - 4)
                {
                    frame.Dispose();
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
        finally
        {
            ReceiveConcurrencyGuard.Exit(ref _activeReceives);
        }
    }

    public async ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default)
    {
        try
        {
            await SendValueAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
        }
        finally
        {
            frame.Dispose();
        }
    }

    public async ValueTask<RpcFrame> ReceiveFrameValueAsync(CancellationToken ct = default)
    {
        var payload = await ReceiveValueAsync(ct).ConfigureAwait(false);
        return new RpcFrame(payload);
    }

    /// <summary>
    /// Closes the connection. This operation is idempotent.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCts.Cancel();
        _frameReadTimeout?.Dispose();
        if (_ownsStream || Volatile.Read(ref _activeReceives) != 0)
        {
            await DisposeStreamAsync(_stream).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StreamConnection));
        }
    }

    private async Task WaitForSendSlotAsync(CancellationToken ct)
    {
        try
        {
            if (ct.CanBeCanceled)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
                await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
                return;
            }

            await _sendLock.WaitAsync(_disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested &&
            Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StreamConnection));
        }
    }

    private void ValidateIncomingLength(int totalLength)
    {
        if (totalLength < MessageFramer.HeaderSize || totalLength > _maxMessageSize)
        {
            throw new InvalidDataException($"Invalid DotBoxD frame length: {totalLength}.");
        }
    }

    private async Task<int> ReadExactAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        try
        {
            while (totalRead < buffer.Length)
            {
                var read = await ReadChunkAsync(buffer.Slice(totalRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return totalRead;
                }

                totalRead += read;
            }
        }
        finally
        {
            _frameReadTimeout?.CancelPendingTimeout();
        }

        return totalRead;
    }

    private async Task<int> ReadChunkAsync(
        Memory<byte> buffer,
        CancellationToken ct)
    {
        var timeout = _frameReadTimeout;
        if (timeout is null)
        {
            return await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        }

        return await timeout.ReadAsync(_stream, buffer, ct, _frameReadIdleTimeout).ConfigureAwait(false);
    }

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
