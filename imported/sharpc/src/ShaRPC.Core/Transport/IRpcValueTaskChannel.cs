using ShaRPC.Core.Buffers;

namespace ShaRPC.Core.Transport;

/// <summary>
/// Optional low-allocation transport contract for channels that can complete send/receive
/// operations without allocating a <see cref="Task"/> for each frame.
/// </summary>
public interface IRpcValueTaskChannel : IRpcChannel
{
    ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default);
}
