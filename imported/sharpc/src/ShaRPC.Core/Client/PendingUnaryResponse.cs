using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

internal class PendingUnaryResponse<TResponse> :
    TaskCompletionSource<TResponse>,
    IPendingResponse
{
    private RpcPeerOutboundInvoker? _directOwner;
    private int _completed;

    public PendingUnaryResponse(int messageId)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        MessageId = messageId;
    }

    public int MessageId { get; }

    public virtual long TimeoutDeadline => long.MaxValue;

    public virtual PendingCancellationKind CancellationKind => PendingCancellationKind.None;

    public bool RegistersStreamingResponse => false;

    public virtual void SetTimeoutDeadline(long deadline)
    {
    }

    public virtual void CancelByCaller()
    {
    }

    public void DisposeResultWhenAvailable()
    {
    }

    public void SetError(Exception error) =>
        CompleteAndSetException(error);

    public void EnableDirectCompletion(RpcPeerOutboundInvoker owner)
    {
        Volatile.Write(ref _directOwner, owner);

        if (Task.IsCompleted)
        {
            CompleteDirect(sendCancel: false);
        }
    }

    public bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        RpcFrame frame,
        RpcStreamReceiver? stream,
        ISerializer serializer)
    {
        try
        {
            if (!response.IsSuccess)
            {
                throw new ShaRpcRemoteException(
                    response.ErrorMessage ?? "Unknown error",
                    response.ErrorType ?? "Unknown");
            }

            if (response.Stream is not null)
            {
                throw new ShaRpcProtocolException(
                    "Response opened a stream for a non-streaming invocation.");
            }

            CompleteAndSetResult(serializer.Deserialize<TResponse>(payload));
        }
        catch (Exception ex)
        {
            CompleteAndSetException(ex);
        }
        finally
        {
            stream?.Cancel();
            frame.Dispose();
        }

        return true;
    }

    public virtual void TrySetCanceled(PendingCancellationKind kind)
    {
        if (!IsDirectCompletion)
        {
            TrySetCanceled();
            return;
        }

        CompleteDirect(sendCancel: true);
        if (kind == PendingCancellationKind.Timeout)
        {
            TrySetException(CreateTimeoutException());
            return;
        }

        TrySetCanceled();
    }

    protected virtual Exception CreateTimeoutException() =>
        new ShaRpcTimeoutException("Request timed out.");

    private bool IsDirectCompletion =>
        Volatile.Read(ref _directOwner) is not null;

    private void CompleteAndSetResult(TResponse response)
    {
        if (IsDirectCompletion)
        {
            CompleteDirect(sendCancel: false);
        }

        TrySetResult(response);
    }

    private void CompleteAndSetException(Exception error)
    {
        if (IsDirectCompletion)
        {
            CompleteDirect(sendCancel: false);
        }

        TrySetException(error);
    }

    private void CompleteDirect(bool sendCancel)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        Volatile.Read(ref _directOwner)?.CompleteUnaryPending(this, sendCancel);
    }
}

internal class CancellablePendingUnaryResponse<TResponse> :
    PendingUnaryResponse<TResponse>
{
    private readonly ShaRpcPendingRequests _owner;
    private int _cancellationKind;

    public CancellablePendingUnaryResponse(ShaRpcPendingRequests owner, int messageId)
        : base(messageId)
    {
        _owner = owner;
    }

    public override PendingCancellationKind CancellationKind =>
        (PendingCancellationKind)Volatile.Read(ref _cancellationKind);

    public override void CancelByCaller() =>
        _owner.TryCancel(MessageId, this, PendingCancellationKind.Caller);

    public override void TrySetCanceled(PendingCancellationKind kind)
    {
        Volatile.Write(ref _cancellationKind, (int)kind);
        base.TrySetCanceled(kind);
    }
}

internal sealed class PendingUnaryResponseWithTimeout<TResponse> :
    CancellablePendingUnaryResponse<TResponse>
{
    private readonly string _service;
    private readonly string _method;
    private long _timeoutDeadline = long.MaxValue;

    public PendingUnaryResponseWithTimeout(
        ShaRpcPendingRequests owner,
        int messageId,
        string service,
        string method)
        : base(owner, messageId)
    {
        _service = service;
        _method = method;
    }

    public override long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public override void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

    protected override Exception CreateTimeoutException() =>
        new ShaRpcTimeoutException($"Request to {_service}.{_method} timed out.");
}
