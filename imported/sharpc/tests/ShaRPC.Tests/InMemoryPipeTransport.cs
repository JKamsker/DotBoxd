using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;

namespace ShaRPC.Tests;

/// <summary>
/// In-memory duplex connection pair backed by <see cref="System.IO.Pipelines.Pipe"/>. Lets two
/// real <see cref="ShaRPC.Core.RpcPeer"/> endpoints — and therefore the generated proxy and
/// dispatcher they drive — run end-to-end through the full framing stack without sockets:
/// deterministic, fast, and able to fragment the byte stream on demand so the frame-reassembly
/// path is exercised exactly as it would be over a real network.
/// </summary>
internal static class InMemoryPipe
{
    /// <summary>
    /// Creates two directly connected <see cref="IRpcChannel"/> instances for peer tests.
    /// <paramref name="writeChunkSize"/> &gt; 0 splits every <c>SendAsync</c> into chunks of that
    /// many bytes, each flushed separately, so the receiver must reassemble a fragmented stream;
    /// 0 writes each frame in a single flush.
    /// </summary>
    public static (PipeConnection Client, PipeConnection Server) CreateConnectionPair(int writeChunkSize = 0)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var clientConnection = new PipeConnection(
            inbound: serverToClient.Reader,
            outbound: clientToServer.Writer,
            remoteEndpoint: "memory://server",
            writeChunkSize);

        var serverConnection = new PipeConnection(
            inbound: clientToServer.Reader,
            outbound: serverToClient.Writer,
            remoteEndpoint: "memory://client",
            writeChunkSize);

        return (clientConnection, serverConnection);
    }
}

/// <summary>
/// One half of an in-memory duplex link. Mirrors <c>TcpConnection</c>: <see cref="SendAsync"/>
/// writes raw frame bytes; <see cref="ReceiveAsync"/> returns exactly one complete length-prefixed
/// frame (including the 4-byte prefix), reassembling it across however many partial reads the pipe
/// hands back.
/// </summary>
internal sealed class PipeConnection : IRpcChannel
{
    private const int MaxMessageSize = 16 * 1024 * 1024;

    private readonly PipeReader _inbound;
    private readonly PipeWriter _outbound;
    private readonly int _writeChunkSize;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    public PipeConnection(PipeReader inbound, PipeWriter outbound, string remoteEndpoint, int writeChunkSize)
    {
        _inbound = inbound;
        _outbound = outbound;
        RemoteEndpoint = remoteEndpoint;
        _writeChunkSize = writeChunkSize;
    }

    public bool IsConnected => !_disposed;

    public string RemoteEndpoint { get; }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PipeConnection));
        }

        // The lock keeps the chunks of one frame contiguous when several callers send at once.
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_writeChunkSize <= 0)
            {
                await _outbound.WriteAsync(data, ct);
            }
            else
            {
                for (var offset = 0; offset < data.Length; offset += _writeChunkSize)
                {
                    var length = Math.Min(_writeChunkSize, data.Length - offset);
                    await _outbound.WriteAsync(data.Slice(offset, length), ct);
                }
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PipeConnection));
        }

        while (true)
        {
            var result = await _inbound.ReadAsync(ct);
            var buffer = result.Buffer;

            if (TryReadFrame(buffer, out var frame, out var consumed))
            {
                _inbound.AdvanceTo(consumed);
                return frame;
            }

            if (result.IsCompleted)
            {
                // Peer finished and there is no complete frame left — mirror TcpConnection's
                // "connection closed" signal so the server/client receive loops exit cleanly.
                _inbound.AdvanceTo(buffer.Start, buffer.End);
                return Payload.Empty;
            }

            // Not enough bytes yet: consume nothing, mark everything examined so the next
            // ReadAsync only returns once more data has arrived.
            _inbound.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>
    /// A frame is <c>[4-byte little-endian total length][payload...]</c>; the length counts the
    /// prefix itself. Copies the full frame (prefix included) into a pooled <see cref="Payload"/> so
    /// the core <c>MessageFramer</c> can re-parse the 9-byte header from it; the caller owns the result.
    /// </summary>
    private static bool TryReadFrame(in ReadOnlySequence<byte> buffer, out Payload frame, out SequencePosition consumed)
    {
        frame = Payload.Empty;
        consumed = buffer.Start;

        if (buffer.Length < 4)
        {
            return false;
        }

        Span<byte> lengthBytes = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lengthBytes);
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        if (totalLength < 4 || totalLength > MaxMessageSize)
        {
            // Mirror the production transports' InvalidDataException contract for malformed lengths.
            throw new InvalidDataException($"Invalid ShaRPC frame length: {totalLength}.");
        }

        if (buffer.Length < totalLength)
        {
            return false;
        }

        var frameSlice = buffer.Slice(0, totalLength);
        var payload = Payload.Rent(totalLength);
        frameSlice.CopyTo(payload.Memory.Span);
        frame = payload;
        consumed = frameSlice.End;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _outbound.CompleteAsync();
        await _inbound.CompleteAsync();
        _sendLock.Dispose();
    }
}

// (PipeClientTransport / PipeServerTransport adapters were removed: peer/host tests use
// CreateConnectionPair + RpcPeer/RpcHost directly, and the production SingleConnectionServerTransport
// / NamedPipeServerTransport for accept-loop coverage.)
