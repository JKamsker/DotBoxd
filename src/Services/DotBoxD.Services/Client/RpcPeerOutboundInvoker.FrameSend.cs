using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    private ValueTask SendOwnedFrameAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        if (_sendFrameAsync is { } sendFrameAsync)
        {
            try
            {
                return sendFrameAsync(frame, ct);
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }

        return SendOwnedFrameByMemoryAsync(frame, ct);
    }

    private async ValueTask SendOwnedFrameByMemoryAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        try
        {
            await _sendAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
        }
        finally
        {
            frame.Dispose();
        }
    }
}
