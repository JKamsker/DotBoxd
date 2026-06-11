namespace SafeIR.Benchmarks.Ipc;

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;

internal static class InMemoryRpcChannel
{
    public static (IRpcChannel Server, IRpcChannel Client) CreatePair()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        return (
            new PipeConnection(clientToServer.Reader, serverToClient.Writer, "memory://client"),
            new PipeConnection(serverToClient.Reader, clientToServer.Writer, "memory://server"));
    }

    private sealed class PipeConnection : IRpcChannel
    {
        private const int MaxMessageSize = 16 * 1024 * 1024;
        private readonly PipeReader _inbound;
        private readonly PipeWriter _outbound;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private int _disposed;

        public PipeConnection(PipeReader inbound, PipeWriter outbound, string remoteEndpoint)
        {
            _inbound = inbound;
            _outbound = outbound;
            RemoteEndpoint = remoteEndpoint;
        }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint { get; }

        public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try {
                await _outbound.WriteAsync(data, ct).ConfigureAwait(false);
            }
            finally {
                _sendLock.Release();
            }
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            while (true) {
                var result = await _inbound.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;

                if (TryReadFrame(buffer, out var frame, out var consumed)) {
                    _inbound.AdvanceTo(consumed);
                    return frame;
                }

                if (result.IsCompleted) {
                    _inbound.AdvanceTo(buffer.Start, buffer.End);
                    return Payload.Empty;
                }

                _inbound.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

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

            if (buffer.Length < 4) {
                return false;
            }

            Span<byte> lengthBytes = stackalloc byte[4];
            buffer.Slice(0, 4).CopyTo(lengthBytes);
            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (totalLength < 4 || totalLength > MaxMessageSize) {
                throw new InvalidDataException($"Invalid ShaRPC frame length: {totalLength}.");
            }

            if (buffer.Length < totalLength) {
                return false;
            }

            var frameSlice = buffer.Slice(0, totalLength);
            frame = Payload.Rent(totalLength);
            frameSlice.CopyTo(frame.Memory.Span);
            consumed = frameSlice.End;
            return true;
        }
    }
}
