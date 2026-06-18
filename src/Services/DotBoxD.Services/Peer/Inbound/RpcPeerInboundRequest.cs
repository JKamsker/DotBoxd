using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Peer.Inbound;

internal readonly struct RpcPeerInboundRequest
{
    public RpcPeerInboundRequest(
        RpcFrame frame,
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> body,
        CancellationTokenSource? requestCts,
        IServiceDispatcher? dispatcher,
        bool requiresStreamingContext)
    {
        Frame = frame;
        Request = request;
        MessageId = messageId;
        Body = body;
        RequestCts = requestCts;
        Dispatcher = dispatcher;
        RequiresStreamingContext = requiresStreamingContext;
    }

    public RpcFrame Frame { get; }

    public RpcRequest Request { get; }

    public int MessageId { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public CancellationTokenSource? RequestCts { get; }

    public IServiceDispatcher? Dispatcher { get; }

    public bool RequiresStreamingContext { get; }

    public CancellationToken CancellationToken =>
        RequestCts?.Token ?? CancellationToken.None;

    public bool IsCancellationRequested =>
        RequestCts?.IsCancellationRequested == true;
}
