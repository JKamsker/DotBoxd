using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core;

internal sealed partial class RpcPeerOutboundInvoker
{
    private Task<TResponse> SendUnaryRequestAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        PendingResponse pending;
        try
        {
            ValidateTarget(service, method);
            _ensureStarted();
            pending = ReservePendingRequest(ct);
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
        PendingResponse pending;
        try
        {
            ValidateTarget(service, method);
            _ensureStarted();
            pending = ReservePendingRequest(ct);
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

    private async Task<TResponse> SendFrameAndReadUnaryResponseAsync<TResponse>(
        int messageId,
        PendingResponse pending,
        PooledBufferWriter frame,
        string service,
        string method,
        CancellationToken ct)
    {
        var responseOwned = false;
        var requestSent = false;
        ReceivedResponse? received = null;
        try
        {
            using (frame)
            {
                await _sendAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
                requestSent = true;
            }

            var callerCancellation = ct.CanBeCanceled
                ? ct.Register(static state => ((PendingResponse)state!).CancelByCaller(), pending)
                : default;
            using (callerCancellation)
            {
                _pending.StartTimeout(pending, _timeout);
                try
                {
                    received = await pending.Task.ConfigureAwait(false);
                    responseOwned = true;
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Timeout)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
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

            if (!received.Response.IsSuccess)
            {
                throw new ShaRpcRemoteException(
                    received.Response.ErrorMessage ?? "Unknown error",
                    received.Response.ErrorType ?? "Unknown");
            }

            EnsureNonStreamingResponse(received);
            return _serializer.Deserialize<TResponse>(received.Payload);
        }
        finally
        {
            received?.Dispose();
            _pending.Remove(messageId, pending, responseOwned);
            ReleasePendingSlot();
        }
    }

    private static Task<T> ToFaultedTask<T>(Exception error) =>
        Task.FromException<T>(error);
}
