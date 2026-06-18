using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Streaming.Core;

internal readonly struct RpcStreamFrameSender(
    Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
    Func<PooledBufferWriter, CancellationToken, ValueTask>? sendFrameAsync)
{
    public async ValueTask SendAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        if (sendFrameAsync is not null)
        {
            await sendFrameAsync(frame, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await sendAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
        }
        finally
        {
            frame.Dispose();
        }
    }
}
