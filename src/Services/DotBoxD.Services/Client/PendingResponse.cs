using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Client;

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
