using DotBoxd.Services.Buffers;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Serialization;
using DotBoxd.Services.Streaming;
using DotBoxd.Services.Transport;

namespace DotBoxd.Services.Client;

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
