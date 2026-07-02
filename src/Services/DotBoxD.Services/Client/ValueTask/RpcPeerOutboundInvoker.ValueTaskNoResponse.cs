using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    public ValueTask InvokeValueAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        SendNoResponseValueRequestAsync(service, method, request, instanceId: null, ct);

    public ValueTask InvokeValueAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        SendNoResponseValueRequestAsync(service, method, instanceId: null, ct);

    public ValueTask InvokeValueOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdValueTask();
        }

        return SendNoResponseValueRequestAsync(service, method, request, instanceId, ct);
    }

    public ValueTask InvokeValueOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdValueTask();
        }

        return SendNoResponseValueRequestAsync(service, method, instanceId, ct);
    }

    private ValueTask SendNoResponseValueRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        if (!CanUseLowAllocationValueTaskPath(ct))
        {
            var task = instanceId is null
                ? InvokeAsync(service, method, request, ct)
                : InvokeOnInstanceAsync(service, instanceId, method, request, ct);
            return new ValueTask(task);
        }

        PendingValueTaskNoResponse pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingValueTaskNoResponseRequest(ct);
        }
        catch (Exception ex)
        {
            return new ValueTask(Task.FromException(ex));
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
            return new ValueTask(Task.FromException(ex));
        }

        return SendFrameAndReadNoResponseValueAsync(pending.MessageId, pending, frame, ct);
    }

    private ValueTask SendNoResponseValueRequestAsync(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        if (!CanUseLowAllocationValueTaskPath(ct))
        {
            var task = instanceId is null
                ? InvokeAsync(service, method, ct)
                : InvokeOnInstanceAsync(service, instanceId, method, ct);
            return new ValueTask(task);
        }

        PendingValueTaskNoResponse pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingValueTaskNoResponseRequest(ct);
        }
        catch (Exception ex)
        {
            return new ValueTask(Task.FromException(ex));
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
            return new ValueTask(Task.FromException(ex));
        }

        return SendFrameAndReadNoResponseValueAsync(pending.MessageId, pending, frame, ct);
    }

    private ValueTask SendFrameAndReadNoResponseValueAsync(
        int messageId,
        PendingValueTaskNoResponse pending,
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
                return new ValueTask(Task.FromException(ex));
            }

            if (sendValueTask.IsCompletedSuccessfully)
            {
                pending.EnableDirectCompletion(this);
                return pending.ValueTask;
            }

            return AwaitNoResponseFrameValueAsync(messageId, pending, sendValueTask);
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
            return new ValueTask(Task.FromException(ex));
        }

        if (sendTask.IsCompletedSuccessfully)
        {
            frame.Dispose();
            pending.EnableDirectCompletion(this);
            return pending.ValueTask;
        }

        return AwaitNoResponseValueAsync(messageId, pending, frame, sendTask);
    }

    private async ValueTask AwaitNoResponseFrameValueAsync(
        int messageId,
        PendingValueTaskNoResponse pending,
        ValueTask sendTask)
    {
        var pendingConsumed = false;
        try
        {
            await sendTask.ConfigureAwait(false);

            try
            {
                await pending.ValueTask.ConfigureAwait(false);
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

    private async ValueTask AwaitNoResponseValueAsync(
        int messageId,
        PendingValueTaskNoResponse pending,
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
                await pending.ValueTask.ConfigureAwait(false);
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
