using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer.Inbound;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Server;

internal sealed partial class RpcPeerInboundDispatcher
{
    public async ValueTask<bool> AcceptRequestAsync(
        RpcFrame frame,
        int messageId,
        CancellationToken loopCt)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return false;
        }

        if (!TryCreateInboundRequest(frame, messageId, loopCt, out var inbound, out var protocolError, out var protocolException))
        {
            if (protocolError is not null)
            {
                _protocolError(messageId, MessageType.Request, protocolError, protocolException);
                using var errorFrame = _responseBuilder.BuildProtocolErrorFrame(messageId, protocolError);
                await _sendAsync(errorFrame.Memory, loopCt).ConfigureAwait(false);
            }

            return false;
        }

        if (_queue is null)
        {
            StartRequest(inbound);
            return true;
        }

        var result = await _queue.EnqueueAsync(inbound, loopCt).ConfigureAwait(false);
        if (result == InboundEnqueueResult.Dropped)
        {
            await SendQueueFullErrorAsync(messageId, loopCt).ConfigureAwait(false);
        }

        return result == InboundEnqueueResult.Accepted;
    }

    public ValueTask<bool> AcceptRequestAsync(
        Payload frame,
        int messageId,
        CancellationToken loopCt) =>
        AcceptRequestAsync(new RpcFrame(frame), messageId, loopCt);

    private async Task SendQueueFullErrorAsync(int messageId, CancellationToken ct)
    {
        try
        {
            using var errorFrame = _responseBuilder.BuildErrorFrame(messageId, RpcErrors.QueueFull());
            await _sendAsync(errorFrame.Memory, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: callers still fall back to their timeout if this cannot be sent.
        }
    }

    private bool TryCreateInboundRequest(
        RpcFrame frame,
        int messageId,
        CancellationToken loopCt,
        out RpcPeerInboundRequest inbound,
        out string? protocolError,
        out Exception? protocolException)
    {
        inbound = default;
        protocolException = null;
        if (!RpcPeerInboundRequestReader.TryRead(
            frame,
            _serializer,
            messageId,
            out var request,
            out var payload,
            out protocolError,
            out protocolException))
        {
            return false;
        }

        if (!RpcStreamValidation.TryValidateInboundHandles(request.Streams, out protocolError))
        {
            return false;
        }

        if (loopCt.IsCancellationRequested)
        {
            protocolError = null;
            return false;
        }

        var dispatcher = _responseBuilder.ResolveDispatcher(request, out var requiresStreamingContext);
        var requestCts = !_disableInboundRequestCancellation ||
            request.Streams is not null ||
            requiresStreamingContext
                ? new CancellationTokenSource()
                : null;
        if (!_activeInbound.TryAdd(messageId, requestCts))
        {
            requestCts?.Dispose();
            protocolError = "Duplicate request message id.";
            return false;
        }

        if (Volatile.Read(ref _stopped) != 0 || loopCt.IsCancellationRequested)
        {
            _activeInbound.Remove(messageId, requestCts);
            requestCts?.Dispose();
            protocolError = null;
            return false;
        }

        inbound = new RpcPeerInboundRequest(
            frame,
            request,
            messageId,
            payload,
            requestCts,
            dispatcher,
            requiresStreamingContext);
        try
        {
            _streams.RegisterInbound(request.Streams, inbound.CancellationToken);
        }
        catch (ServiceProtocolException ex)
        {
            _activeInbound.Remove(messageId, requestCts);
            requestCts?.Dispose();
            inbound = default;
            protocolError = ex.Message;
            protocolException = ex;
            return false;
        }

        return true;
    }
}
