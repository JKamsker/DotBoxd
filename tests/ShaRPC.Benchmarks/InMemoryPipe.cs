using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;

namespace ShaRPC.Benchmarks;

internal static class InMemoryPipe
{
    public static (PipeConnection Left, PipeConnection Right) CreateConnectionPair()
    {
        var leftToRight = new Pipe();
        var rightToLeft = new Pipe();

        return (
            new PipeConnection(rightToLeft.Reader, leftToRight.Writer, "memory://right"),
            new PipeConnection(leftToRight.Reader, rightToLeft.Writer, "memory://left"));
    }
}

internal sealed class PipeConnection : IRpcChannel
{
    private const int MaxMessageSize = 16 * 1024 * 1024;
    private readonly PipeReader _inbound;
    private readonly PipeWriter _outbound;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    public PipeConnection(PipeReader inbound, PipeWriter outbound, string remoteEndpoint)
    {
        _inbound = inbound;
        _outbound = outbound;
        RemoteEndpoint = remoteEndpoint;
    }

    public bool IsConnected => !_disposed;

    public string RemoteEndpoint { get; }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _outbound.WriteAsync(data, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            var result = await _inbound.ReadAsync(ct).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (TryReadFrame(buffer, out var frame, out var consumed))
            {
                _inbound.AdvanceTo(consumed);
                return frame;
            }

            if (result.IsCompleted)
            {
                _inbound.AdvanceTo(buffer.Start, buffer.End);
                return Payload.Empty;
            }

            _inbound.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _outbound.CompleteAsync().ConfigureAwait(false);
        await _inbound.CompleteAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    private static bool TryReadFrame(
        in ReadOnlySequence<byte> buffer,
        out Payload frame,
        out SequencePosition consumed)
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
            throw new InvalidDataException($"Invalid ShaRPC frame length: {totalLength}.");
        }

        if (buffer.Length < totalLength)
        {
            return false;
        }

        var frameSlice = buffer.Slice(0, totalLength);
        frame = Payload.Rent(totalLength);
        frameSlice.CopyTo(frame.Memory.Span);
        consumed = frameSlice.End;
        return true;
    }
}
