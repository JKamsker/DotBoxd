using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core.Client;

internal sealed class PendingUnaryResponse<TResponse> :
    TaskCompletionSource<TResponse>,
    IPendingResponse
{
    private readonly ShaRpcPendingRequests _owner;
    private RpcPeerOutboundInvoker? _directOwner;
    private string? _service;
    private string? _method;
    private long _timeoutDeadline = long.MaxValue;
    private int _cancellationKind;
    private int _completed;

    public PendingUnaryResponse(ShaRpcPendingRequests owner, int messageId)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        _owner = owner;
        MessageId = messageId;
    }

    public int MessageId { get; }

    public long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public PendingCancellationKind CancellationKind =>
        (PendingCancellationKind)Volatile.Read(ref _cancellationKind);

    public bool RegistersStreamingResponse => false;

    public void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

    public void CancelByCaller() =>
        _owner.TryCancel(MessageId, this, PendingCancellationKind.Caller);

    public void DisposeResultWhenAvailable()
    {
    }

    public void SetError(Exception error) =>
        CompleteAndSetException(error);

    public void EnableDirectCompletion(
        RpcPeerOutboundInvoker owner,
        string service,
        string method)
    {
        _service = service;
        _method = method;
        Volatile.Write(ref _directOwner, owner);

        if (Task.IsCompleted)
        {
            CompleteDirect(sendCancel: false);
        }
    }

    public bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        Payload frame,
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

    public void TrySetCanceled(PendingCancellationKind kind)
    {
        Volatile.Write(ref _cancellationKind, (int)kind);
        if (!IsDirectCompletion)
        {
            TrySetCanceled();
            return;
        }

        CompleteDirect(sendCancel: true);
        if (kind == PendingCancellationKind.Timeout)
        {
            TrySetException(new ShaRpcTimeoutException(
                $"Request to {_service}.{_method} timed out."));
            return;
        }

        TrySetException(new OperationCanceledException());
    }

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
