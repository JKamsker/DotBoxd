using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core;

internal sealed partial class RpcPeerOutboundInvoker
{
    public ValueTask<TResponse> InvokeValueAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        SendUnaryValueRequestAsync<TRequest, TResponse>(service, method, request, instanceId: null, ct);

    public ValueTask<TResponse> InvokeValueAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default) =>
        SendUnaryValueRequestAsync<TResponse>(service, method, instanceId: null, ct);

    public ValueTask<TResponse> InvokeValueOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        SendUnaryValueRequestAsync<TRequest, TResponse>(service, method, request, instanceId, ct);

    public ValueTask<TResponse> InvokeValueOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        SendUnaryValueRequestAsync<TResponse>(service, method, instanceId, ct);

    private ValueTask<TResponse> SendUnaryValueRequestAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        if (!CanUseLowAllocationValueTaskPath(ct))
        {
            return new ValueTask<TResponse>(
                SendUnaryRequestAsync<TRequest, TResponse>(service, method, request, instanceId, ct));
        }

        PendingValueTaskUnaryResponse<TResponse> pending;
        try
        {
            ValidateTarget(service, method);
            _ensureStarted();
            pending = ReservePendingValueTaskUnaryRequest<TResponse>(ct);
        }
        catch (Exception ex)
        {
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
        }

        PooledBufferWriter frame;
        try
        {
            var envelope = CreateEnvelope(pending.MessageId, service, method, instanceId, streams: null);
            frame = MessageFramer.RentFrameRequest(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                request);
        }
        catch (Exception ex)
        {
            _pending.Remove(pending.MessageId, pending, consumed: true);
            ReleasePendingSlot();
            pending.Abandon();
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
        }

        return SendFrameAndReadUnaryValueResponseAsync<TResponse>(
            pending.MessageId,
            pending,
            frame,
            ct);
    }

    private ValueTask<TResponse> SendUnaryValueRequestAsync<TResponse>(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        if (!CanUseLowAllocationValueTaskPath(ct))
        {
            return new ValueTask<TResponse>(
                SendUnaryRequestAsync<TResponse>(service, method, instanceId, ct));
        }

        PendingValueTaskUnaryResponse<TResponse> pending;
        try
        {
            ValidateTarget(service, method);
            _ensureStarted();
            pending = ReservePendingValueTaskUnaryRequest<TResponse>(ct);
        }
        catch (Exception ex)
        {
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
        }

        PooledBufferWriter frame;
        try
        {
            var envelope = CreateEnvelope(pending.MessageId, service, method, instanceId, streams: null);
            frame = MessageFramer.RentFrameMessage(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                ReadOnlySpan<byte>.Empty);
        }
        catch (Exception ex)
        {
            _pending.Remove(pending.MessageId, pending, consumed: true);
            ReleasePendingSlot();
            pending.Abandon();
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
        }

        return SendFrameAndReadUnaryValueResponseAsync<TResponse>(
            pending.MessageId,
            pending,
            frame,
            ct);
    }

    private bool CanUseLowAllocationValueTaskPath(CancellationToken ct) =>
        _enableLowAllocationValueTaskInvocations &&
        !ct.CanBeCanceled &&
        _timeout == Timeout.InfiniteTimeSpan;

    private ValueTask<TResponse> SendFrameAndReadUnaryValueResponseAsync<TResponse>(
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
        PooledBufferWriter frame,
        CancellationToken ct)
    {
        var sendFrameAsync = _sendFrameAsync;
        if (sendFrameAsync is not null)
        {
            ValueTask sendValueTask;
            try
            {
                sendValueTask = sendFrameAsync(frame, ct);
            }
            catch (Exception ex)
            {
                frame.Dispose();
                _pending.Remove(messageId, pending, consumed: true);
                ReleasePendingSlot();
                pending.Abandon();
                return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
            }

            if (sendValueTask.IsCompletedSuccessfully)
            {
                pending.EnableDirectCompletion(this);
                return pending.ValueTask;
            }

            return AwaitUnaryFrameValueResponseAsync(messageId, pending, sendValueTask);
        }

        Task sendTask;
        try
        {
            sendTask = _sendAsync(frame.WrittenMemory, ct);
        }
        catch (Exception ex)
        {
            frame.Dispose();
            _pending.Remove(messageId, pending, consumed: true);
            ReleasePendingSlot();
            pending.Abandon();
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
        }

        if (sendTask.IsCompletedSuccessfully)
        {
            frame.Dispose();
            pending.EnableDirectCompletion(this);
            return pending.ValueTask;
        }

        return AwaitUnaryValueResponseAsync(messageId, pending, frame, sendTask);
    }

    private async ValueTask<TResponse> AwaitUnaryFrameValueResponseAsync<TResponse>(
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
        ValueTask sendTask)
    {
        var pendingConsumed = false;
        try
        {
            await sendTask.ConfigureAwait(false);

            try
            {
                return await pending.ValueTask.ConfigureAwait(false);
            }
            finally
            {
                pendingConsumed = true;
            }
        }
        finally
        {
            _pending.Remove(messageId, pending, pendingConsumed);
            ReleasePendingSlot();
            if (!pendingConsumed)
            {
                pending.Abandon();
            }
        }
    }

    private async ValueTask<TResponse> AwaitUnaryValueResponseAsync<TResponse>(
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
        PooledBufferWriter frame,
        Task sendTask)
    {
        var pendingConsumed = false;
        try
        {
            using (frame)
            {
                await sendTask.ConfigureAwait(false);
            }

            try
            {
                return await pending.ValueTask.ConfigureAwait(false);
            }
            finally
            {
                pendingConsumed = true;
            }
        }
        finally
        {
            _pending.Remove(messageId, pending, pendingConsumed);
            ReleasePendingSlot();
            if (!pendingConsumed)
            {
                pending.Abandon();
            }
        }
    }
}
