using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>
/// TCP-based connection implementation.
/// </summary>
public sealed class TcpConnection : IRpcFrameChannel
{
    /// <summary>Default inter-read idle timeout applied to an in-progress frame read (30 seconds).</summary>
    public static readonly TimeSpan DefaultFrameReadIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly TimeSpan _frameReadIdleTimeout;
    private readonly FrameReadTimeoutSource? _frameReadTimeout;
    private readonly byte[] _lengthBuffer = new byte[4];
    private int _disposed;

    public TcpConnection(TcpClient client) : this(client, null)
    {
    }

    /// <summary>
    /// Creates a TCP connection. <paramref name="frameReadIdleTimeout"/> bounds how long an
    /// in-progress frame read may stall with no data before the connection is torn down — defending
    /// against a slow-loris peer that declares a large frame then trickles (or sends nothing),
    /// pinning a connection and a rented buffer. It is NOT applied while idly awaiting the first byte
    /// of the next frame, so legitimately idle connections are unaffected. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable; <see langword="null"/> uses
    /// <see cref="DefaultFrameReadIdleTimeout"/>.
    /// </summary>
    public TcpConnection(TcpClient client, TimeSpan? frameReadIdleTimeout)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        var timeout = frameReadIdleTimeout ?? DefaultFrameReadIdleTimeout;
        if (timeout != Timeout.InfiniteTimeSpan &&
            (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameReadIdleTimeout),
                timeout,
                "Frame read idle timeout must be positive (at most int.MaxValue ms) or Timeout.InfiniteTimeSpan.");
        }

        _frameReadIdleTimeout = timeout;
        _frameReadTimeout = timeout == Timeout.InfiniteTimeSpan ? null : new FrameReadTimeoutSource();
        _client.NoDelay = true;
        _stream = client.GetStream();
        // Capture the endpoint once: after DisposeAsync closes the client its underlying socket is
        // disposed, so reading RemoteEndPoint live would throw ObjectDisposedException from logging
        // or a Disconnected handler. Mirrors StreamConnection's cached endpoint.
        RemoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Whether this connection is believed to be live. This is a best-effort <em>hint</em>: it
    /// combines the disposed flag with <see cref="System.Net.Sockets.TcpClient.Connected"/>, which
    /// reflects only the last known socket state and does not probe the wire. A dropped connection
    /// is not observed here until the next send/receive fails — rely on I/O exceptions for the
    /// authoritative state.
    /// </summary>
    public bool IsConnected => _client.Connected && Volatile.Read(ref _disposed) == 0;

    public string RemoteEndpoint { get; }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        SendValueAsync(data, ct).AsTask();

    public async ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }

        // Reject malformed/oversized frames locally rather than shipping them to the peer, matching
        // StreamConnection and the inbound length check in ReceiveAsync below.
        MessageFramer.ValidateOutgoingFrame(data.Span);

        await WaitForSendSlotAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // DisposeAsync disposed the send lock while this send was in flight; the real
                // I/O fault (if any) already propagates from the WriteAsync above.
            }
        }
    }

    public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
        ReceiveValueAsync(ct).AsTask();

    public async ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }

        // Read length prefix (4 bytes). Keep this per connection instead of renting
        // a tiny ArrayPool buffer for every received frame.
        var lengthBuffer = _lengthBuffer;
        var bytesRead = await ReadExactAsync(lengthBuffer.AsMemory(0, 4), ct, timeFirstRead: false)
            .ConfigureAwait(false);
        if (bytesRead < 4)
        {
            return Payload.Empty; // Connection closed
        }

        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer.AsSpan(0, 4));

        // A valid frame is at least a full header (length prefix + type + message id). Rejecting
        // sub-header lengths (1-3) before renting also avoids the Slice(0, 4) below throwing on a
        // too-small buffer and leaking it. Mirrors StreamConnection.ValidateIncomingLength.
        if (totalLength < MessageFramer.HeaderSize || totalLength > MessageFramer.MaxMessageSize)
        {
            // A malformed length from the peer is invalid inbound DATA, not a local state error.
            // Matches StreamConnection.ValidateIncomingLength and MessageFramer.ReadMessageAsync so
            // the IRpcChannel contract surfaces one exception type across every transport.
            throw new InvalidDataException($"Invalid DotBoxD frame length: {totalLength}.");
        }

        // Rent the full frame buffer and write back the length prefix we already consumed.
        var payload = Payload.Rent(totalLength);
        try
        {
            lengthBuffer.AsSpan(0, 4).CopyTo(payload.Memory.Span);

            // The header has fully arrived, so a frame is in progress: time every body read so a
            // peer that stalls mid-frame cannot pin this rented buffer indefinitely.
            bytesRead = await ReadExactAsync(payload.Memory.Slice(4), ct, timeFirstRead: true)
                .ConfigureAwait(false);
            if (bytesRead < totalLength - 4)
            {
                throw new InvalidDataException(
                    $"Connection closed after {bytesRead} of {totalLength - 4} frame bytes.");
            }
        }
        catch
        {
            payload.Dispose();
            throw;
        }

        return payload;
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

    private async Task<int> ReadExactAsync(Memory<byte> buffer, CancellationToken ct, bool timeFirstRead)
    {
        var totalRead = 0;
        try
        {
            while (totalRead < buffer.Length)
            {
                // Apply the idle timeout once a frame is in progress: always for body reads, and for the
                // length prefix only after its first byte has arrived (the initial wait is idle, not a stall).
                var applyTimeout = timeFirstRead || totalRead > 0;
                var read = await ReadChunkAsync(buffer.Slice(totalRead), ct, applyTimeout)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return totalRead; // Connection closed
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
        CancellationToken ct,
        bool applyTimeout)
    {
        var timeout = _frameReadTimeout;
        if (!applyTimeout || timeout is null)
        {
            return await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        }

        var readToken = timeout.Start(ct, _frameReadIdleTimeout);
        try
        {
            return await _stream.ReadAsync(buffer, readToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsTimeoutCancellation(ct))
        {
            throw new IOException(
                $"Inbound frame read stalled for longer than {_frameReadIdleTimeout} with no data (possible slow-loris peer).");
        }
        finally
        {
            timeout.CancelPendingTimeout();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return default;
        }

        _disposeCts.Cancel();
        _frameReadTimeout?.Dispose();
        try
        {
            _stream.Close();
            _client.Close();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        return default;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
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
            throw new ObjectDisposedException(nameof(TcpConnection));
        }
    }
}
