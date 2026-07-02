using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    private Task<TResponse> SendUnaryRequestAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        PendingUnaryResponse<TResponse> pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingUnaryRequest<TResponse>(service, method, ct);
        }
        catch (Exception ex)
        {
            return ToFaultedTask<TResponse>(ex);
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
            return ToFaultedTask<TResponse>(ex);
        }

        return SendFrameAndReadUnaryResponseAsync<TResponse>(
            pending.MessageId,
            pending,
            frame,
            service,
            method,
            ct);
    }

    private Task<TResponse> SendUnaryRequestAsync<TResponse>(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        PendingUnaryResponse<TResponse> pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingUnaryRequest<TResponse>(service, method, ct);
        }
        catch (Exception ex)
        {
            return ToFaultedTask<TResponse>(ex);
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
            return ToFaultedTask<TResponse>(ex);
        }

        return SendFrameAndReadUnaryResponseAsync<TResponse>(
            pending.MessageId,
            pending,
            frame,
            service,
            method,
            ct);
    }

    private Task<TResponse> SendFrameAndReadUnaryResponseAsync<TResponse>(
        int messageId,
        PendingUnaryResponse<TResponse> pending,
        PooledBufferWriter frame,
        string service,
        string method,
        CancellationToken ct)
    {
        ValueTask sendTask;
        try
        {
            sendTask = SendOwnedFrameAsync(frame, ct);
        }
        catch (Exception ex)
        {
            _pending.Remove(messageId, pending, consumed: true);
            ReleasePendingSlot();
            return ToFaultedTask<TResponse>(ex);
        }

        if (!ct.CanBeCanceled && sendTask.IsCompletedSuccessfully)
        {
            pending.EnableDirectCompletion(this);
            if (!pending.Task.IsCompleted)
            {
                _pending.StartTimeout(pending, _timeout);
            }

            return pending.Task;
        }

        return AwaitUnaryResponseAsync(
            messageId,
            pending,
            sendTask,
            service,
            method,
            ct);
    }

    private async Task<TResponse> AwaitUnaryResponseAsync<TResponse>(
        int messageId,
        PendingUnaryResponse<TResponse> pending,
        ValueTask sendTask,
        string service,
        string method,
        CancellationToken ct)
    {
        var responseOwned = false;
        var requestSent = false;
        try
        {
            await sendTask.ConfigureAwait(false);
            requestSent = true;

            var callerCancellation = ct.CanBeCanceled
                ? ct.Register(static state => ((IPendingResponse)state!).CancelByCaller(), pending)
                : default;
            using (callerCancellation)
            {
                _pending.StartTimeout(pending, _timeout);
                try
                {
                    var response = await pending.Task.ConfigureAwait(false);
                    responseOwned = true;
                    return response;
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Timeout)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    throw new ServiceTimeoutException($"Request to {service}.{method} timed out.");
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Caller)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    throw;
                }
            }

        }
        finally
        {
            _pending.Remove(messageId, pending, responseOwned);
            ReleasePendingSlot();
        }
    }

    private static Task<T> ToFaultedTask<T>(Exception error) =>
        Task.FromException<T>(error);
}
