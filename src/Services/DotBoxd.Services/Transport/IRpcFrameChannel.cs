using DotBoxd.Services.Buffers;

namespace DotBoxd.Services.Transport;

/// <summary>
/// Optional low-allocation transport contract for channels that can transfer ownership
/// of complete pooled frames instead of copying them into a new <see cref="Payload"/>.
/// </summary>
public interface IRpcFrameChannel : IRpcValueTaskChannel
{
    ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default);

    ValueTask<RpcFrame> ReceiveFrameValueAsync(CancellationToken ct = default);
}
