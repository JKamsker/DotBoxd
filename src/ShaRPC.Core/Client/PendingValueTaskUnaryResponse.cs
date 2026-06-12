using System.Threading.Tasks.Sources;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

internal sealed class PendingValueTaskUnaryResponse<TResponse> :
    IPendingResponse,
    IValueTaskSource<TResponse>
{
    private static readonly object PoolGate = new();
    private static PendingValueTaskUnaryResponse<TResponse>? s_pool;

    private ManualResetValueTaskSourceCore<TResponse> _source;
    private PendingValueTaskUnaryResponse<TResponse>? _next;
    private RpcPeerOutboundInvoker? _directOwner;
    private int _messageId;
    private int _completed;
    private int _returned;
    private int _valueTaskIssued;

    private PendingValueTaskUnaryResponse()
    {
        // The no-timeout ValueTask fast path avoids the Task allocation by completing inline.
        _source.RunContinuationsAsynchronously = false;
    }

    public int MessageId => _messageId;

    public long TimeoutDeadline => long.MaxValue;

    public PendingCancellationKind CancellationKind => PendingCancellationKind.None;

    public bool RegistersStreamingResponse => false;

    public static PendingValueTaskUnaryResponse<TResponse> Rent(int messageId)
    {
        PendingValueTaskUnaryResponse<TResponse>? pending;
        lock (PoolGate)
        {
            pending = s_pool;
            if (pending is not null)
            {
                s_pool = pending._next;
                pending._next = null;
            }
        }

        pending ??= new PendingValueTaskUnaryResponse<TResponse>();
        pending.Reset(messageId);
        return pending;
    }

    public ValueTask<TResponse> ValueTask
    {
        get
        {
            Volatile.Write(ref _valueTaskIssued, 1);
            return new ValueTask<TResponse>(this, _source.Version);
        }
    }

    public void SetTimeoutDeadline(long deadline)
    {
    }

    public void CancelByCaller()
    {
    }

    public void DisposeResultWhenAvailable()
    {
    }

    public void SetError(Exception error)
    {
        CompleteDirect(sendCancel: false);
        _source.SetException(error);
    }

    public void EnableDirectCompletion(RpcPeerOutboundInvoker owner)
    {
        Volatile.Write(ref _directOwner, owner);

        if (_source.GetStatus(_source.Version) != ValueTaskSourceStatus.Pending)
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
            SetError(ex);
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
        CompleteDirect(sendCancel: true);
        _source.SetException(new OperationCanceledException());
    }

    public TResponse GetResult(short token)
    {
        try
        {
            return _source.GetResult(token);
        }
        finally
        {
            Return();
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) =>
        _source.GetStatus(token);

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);

    public void Abandon()
    {
        if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
        {
            _source.SetException(new ShaRpcConnectionException("Request abandoned."));
        }

        if (Volatile.Read(ref _valueTaskIssued) == 0)
        {
            Return();
        }
    }

    private void Reset(int messageId)
    {
        _messageId = messageId;
        _directOwner = null;
        _completed = 0;
        _returned = 0;
        _valueTaskIssued = 0;
    }

    private void CompleteAndSetResult(TResponse response)
    {
        if (Volatile.Read(ref _directOwner) is not null)
        {
            CompleteDirect(sendCancel: false);
        }

        _source.SetResult(response);
    }

    private void CompleteDirect(bool sendCancel)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        Volatile.Read(ref _directOwner)?.CompleteUnaryPending(this, sendCancel);
    }

    private void Return()
    {
        if (Interlocked.Exchange(ref _returned, 1) != 0)
        {
            return;
        }

        ClearForPool();
        lock (PoolGate)
        {
            _next = s_pool;
            s_pool = this;
        }
    }

    private void ClearForPool()
    {
        _source.Reset();
        _messageId = 0;
        _directOwner = null;
        _completed = 0;
        _valueTaskIssued = 0;
    }
}
