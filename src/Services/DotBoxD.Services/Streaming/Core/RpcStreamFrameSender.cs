using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Streaming.Core;

internal readonly struct RpcStreamFrameSender(
    Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
    Func<PooledBufferWriter, CancellationToken, ValueTask>? sendFrameAsync)
{
    public async ValueTask SendAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        try
        {
            MessageFramer.ValidateOutgoingFrame(frame.WrittenSpan);
        }
        catch
        {
            frame.Dispose();
            throw;
        }

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
