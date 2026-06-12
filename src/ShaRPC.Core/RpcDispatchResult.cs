using ShaRPC.Core.Buffers;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core;

internal struct RpcDispatchResult : IDisposable
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

    public bool TryDetachWriter(out PooledBufferWriter writer)
    {
        if (_writerFrame is null)
        {
            writer = null!;
            return false;
        }

        writer = _writerFrame;
        _writerFrame = null;
        return true;
    }

    public void Dispose()
    {
        _payloadFrame?.Dispose();
        _payloadFrame = null;
        _writerFrame?.Dispose();
        _writerFrame = null;
    }
}
