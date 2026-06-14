using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal readonly struct RpcPeerInboundRequest
{
    public RpcPeerInboundRequest(
        RpcFrame frame,
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> body,
        CancellationTokenSource? requestCts)
    {
        Frame = frame;
        Request = request;
        MessageId = messageId;
        Body = body;
        RequestCts = requestCts;
    }

    public RpcFrame Frame { get; }

    public RpcRequest Request { get; }

    public int MessageId { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public CancellationTokenSource? RequestCts { get; }

    public CancellationToken CancellationToken =>
        RequestCts?.Token ?? CancellationToken.None;

    public bool IsCancellationRequested =>
        RequestCts?.IsCancellationRequested == true;
}
