using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

internal sealed class PendingReceivedResponse :
    TaskCompletionSource<ReceivedResponse>,
    IPendingResponse
{
    private readonly ShaRpcPendingRequests _owner;
    private long _timeoutDeadline = long.MaxValue;
    private int _cancellationKind;

    public PendingReceivedResponse(ShaRpcPendingRequests owner, int messageId)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        _owner = owner;
        MessageId = messageId;
    }

    public int MessageId { get; }

    public long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public PendingCancellationKind CancellationKind =>
        (PendingCancellationKind)Volatile.Read(ref _cancellationKind);

    public bool RegistersStreamingResponse => true;

    public void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

    public void CancelByCaller() =>
        _owner.TryCancel(MessageId, this, PendingCancellationKind.Caller);

    public void DisposeResultWhenAvailable() =>
        ReceivedResponse.DisposeWhenAvailable(Task);

    public void SetError(Exception error) =>
        TrySetException(error);

    public bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        RpcFrame frame,
        RpcStreamReceiver? stream,
        ISerializer serializer)
    {
        var received = new ReceivedResponse(response, payload, frame, stream);
        if (!TrySetResult(received))
        {
            received.Dispose();
        }

        return true;
    }

    public void TrySetCanceled(PendingCancellationKind kind)
    {
        Volatile.Write(ref _cancellationKind, (int)kind);
        TrySetCanceled();
    }
}
