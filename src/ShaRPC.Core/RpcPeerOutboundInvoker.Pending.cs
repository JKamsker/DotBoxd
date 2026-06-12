using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;

namespace ShaRPC.Core;

internal sealed partial class RpcPeerOutboundInvoker
{
    private PendingReceivedResponse ReservePendingRequest(CancellationToken ct)
    {
        if (!TryEnterPendingSlot())
        {
            throw new ShaRpcException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = NextMessageId(ct);
                if (messageId != 0 && _pending.TryAdd(messageId, out var pending))
                {
                    return pending;
                }
            }

            throw new ShaRpcException("Unable to reserve a request message id.");
        }
        catch
        {
            ReleasePendingSlot();
            throw;
        }
    }

    private PendingUnaryResponse<TResponse> ReservePendingUnaryRequest<TResponse>(
        string service,
        string method,
        CancellationToken ct)
    {
        if (!TryEnterPendingSlot())
        {
            throw new ShaRpcException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = NextMessageId(ct);
                if (messageId != 0 &&
                    _pending.TryAddUnary<TResponse>(
                        messageId,
                        ct.CanBeCanceled,
                        _timeout != Timeout.InfiniteTimeSpan,
                        service,
                        method,
                        out var pending))
                {
                    return pending;
                }
            }

            throw new ShaRpcException("Unable to reserve a request message id.");
        }
        catch
        {
            ReleasePendingSlot();
            throw;
        }
    }

    private PendingValueTaskUnaryResponse<TResponse> ReservePendingValueTaskUnaryRequest<TResponse>(
        CancellationToken ct)
    {
        if (!TryEnterPendingSlot())
        {
            throw new ShaRpcException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = NextMessageId(ct);
                if (messageId != 0 && _pending.TryAddValueTaskUnary<TResponse>(messageId, out var pending))
                {
                    return pending;
                }
            }

            throw new ShaRpcException("Unable to reserve a request message id.");
        }
        catch
        {
            ReleasePendingSlot();
            throw;
        }
    }

    private bool TryEnterPendingSlot()
    {
        if (Interlocked.Increment(ref _pendingCount) <= _maxPendingRequests)
        {
            return true;
        }

        Interlocked.Decrement(ref _pendingCount);
        return false;
    }

    private void ReleasePendingSlot() =>
        Interlocked.Decrement(ref _pendingCount);

    internal void CompleteUnaryPending(IPendingResponse pending, bool sendCancel)
    {
        if (sendCancel)
        {
            _cancelFrames.TrySend(pending.MessageId);
        }

        ReleasePendingSlot();
    }

    private int NextMessageId(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var messageId = Interlocked.Increment(ref _messageIdCounter);
            if (messageId != 0)
            {
                return messageId;
            }
        }
    }
}
