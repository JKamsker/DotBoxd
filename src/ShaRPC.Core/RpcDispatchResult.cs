using ShaRPC.Core.Buffers;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core;

internal sealed class RpcDispatchResult : IDisposable
{
    private Payload? _payloadFrame;
    private PooledBufferWriter? _writerFrame;

    public RpcDispatchResult(Payload frame, RpcStreamAttachment? stream)
    {
        _payloadFrame = frame;
        Stream = stream;
    }

    public RpcDispatchResult(PooledBufferWriter frame, RpcStreamAttachment? stream)
    {
        _writerFrame = frame;
        Stream = stream;
    }

    public ReadOnlyMemory<byte> FrameMemory
    {
        get
        {
            if (_payloadFrame is { } payloadFrame)
            {
                return payloadFrame.Memory;
            }

            if (_writerFrame is { } writerFrame)
            {
                return writerFrame.WrittenMemory;
            }

            throw new ObjectDisposedException(nameof(RpcDispatchResult));
        }
    }

    public RpcStreamAttachment? Stream { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _payloadFrame, null)?.Dispose();
        Interlocked.Exchange(ref _writerFrame, null)?.Dispose();
    }
}
