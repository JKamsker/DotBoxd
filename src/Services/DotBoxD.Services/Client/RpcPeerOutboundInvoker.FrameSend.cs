using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Streaming.Core;

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

    private async Task<ReceivedResponse> SendFrameAndAwaitAsync(
        int messageId,
        PendingReceivedResponse pending,
        PooledBufferWriter frame,
        string service,
        string method,
        RpcOutboundStreamSet outboundStreams,
        CancellationToken ct)
    {
        var consumed = false;
        var requestSent = false;
        try
        {
            await SendOwnedFrameAsync(frame, ct).ConfigureAwait(false);
            requestSent = true;
            outboundStreams.Start();

            var callerCancellation = ct.CanBeCanceled
                ? ct.Register(static state => ((IPendingResponse)state!).CancelByCaller(), pending)
                : default;
            using (callerCancellation)
            {
                _pending.StartTimeout(pending, _timeout);
                try
                {
                    var received = await pending.Task.ConfigureAwait(false);
                    if (!received.Response.IsSuccess)
                    {
                        ThrowRemoteError(received);
                    }

                    received.AttachOutboundStreams(outboundStreams);
                    consumed = true;
                    return received;
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Timeout)
                {
                    TrySendCancelForSentRequest(requestSent, messageId);
                    ct.ThrowIfCancellationRequested();
                    throw new ServiceTimeoutException($"Request to {service}.{method} timed out.");
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Caller)
                {
                    TrySendCancelForSentRequest(requestSent, messageId);
                    ct.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }
        finally
        {
            _pending.Remove(messageId, pending, consumed);
            ReleasePendingSlot();
            if (!consumed)
            {
                await outboundStreams.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void TrySendCancelForSentRequest(bool requestSent, int messageId)
    {
        if (requestSent)
        {
            _cancelFrames.TrySend(messageId);
        }
    }

    private static void ThrowRemoteError(ReceivedResponse received)
    {
        var error = new RemoteServiceException(
            received.Response.ErrorMessage ?? "Unknown error",
            received.Response.ErrorType ?? "Unknown");
        received.Dispose();
        throw error;
    }
}
