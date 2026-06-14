using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

internal enum PendingCancellationKind
{
    None,
    Caller,
    Timeout,
}

internal interface IPendingResponse
{
    int MessageId { get; }

    long TimeoutDeadline { get; }

    PendingCancellationKind CancellationKind { get; }

    bool RegistersStreamingResponse { get; }

    void SetTimeoutDeadline(long deadline);

    void CancelByCaller();

    void DisposeResultWhenAvailable();

    void SetError(Exception error);

    bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        RpcFrame frame,
        RpcStreamReceiver? stream,
        ISerializer serializer);

    void TrySetCanceled(PendingCancellationKind kind);
}
