using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.Tcp;

/// <summary>
/// TCP-based connection implementation.
/// </summary>
public sealed class TcpConnection : IRpcChannel
{
    /// <summary>Default inter-read idle timeout applied to an in-progress frame read (30 seconds).</summary>
    public static readonly TimeSpan DefaultFrameReadIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly TimeSpan _frameReadIdleTimeout;
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

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }

        // Reject malformed/oversized frames locally rather than shipping them to the peer, matching
        // StreamConnection and the inbound length check in ReceiveAsync below.
        MessageFramer.ValidateOutgoingFrame(data.Span);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
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

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }

        // Read length prefix (4 bytes)
        var lengthBuffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            // The first read of the length prefix waits for the next frame and is not timed (an idle
            // connection is legitimate); once any byte arrives, the rest of the frame is timed.
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
                throw new InvalidOperationException($"Invalid message length: {totalLength}");
            }

            // Rent the full frame buffer and write back the length prefix we already consumed.
            var payload = Payload.Rent(totalLength);
            try
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.Memory.Span.Slice(0, 4), totalLength);

                // The header has fully arrived, so a frame is in progress: time every body read so a
                // peer that stalls mid-frame cannot pin this rented buffer indefinitely.
                bytesRead = await ReadExactAsync(payload.Memory.Slice(4), ct, timeFirstRead: true)
                    .ConfigureAwait(false);
                if (bytesRead < totalLength - 4)
                {
                    payload.Dispose();
                    return Payload.Empty; // Connection closed
                }
            }
            catch
            {
                payload.Dispose();
                throw;
            }

            return payload;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }

    private async Task<int> ReadExactAsync(Memory<byte> buffer, CancellationToken ct, bool timeFirstRead)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            // Apply the idle timeout once a frame is in progress: always for body reads, and for the
            // length prefix only after its first byte has arrived (the initial wait is idle, not a stall).
            var read = await ReadChunkAsync(buffer.Slice(totalRead), ct, applyTimeout: timeFirstRead || totalRead > 0)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead; // Connection closed
            }
            totalRead += read;
        }
        return totalRead;
    }

    private async Task<int> ReadChunkAsync(Memory<byte> buffer, CancellationToken ct, bool applyTimeout)
    {
        if (!applyTimeout || _frameReadIdleTimeout == Timeout.InfiniteTimeSpan)
        {
            return await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return default;
        }

        try
        {
            _stream.Close();
            _client.Close();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _sendLock.Dispose();
        return default;
    }
}
