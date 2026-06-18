using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Remote;

namespace DotBoxD.Services.Peer;

internal sealed class RpcPeerResponseBuilder
{
    private readonly RpcDispatchResponseBuilder _inner;
    private readonly InstanceRegistry _registry;
    private readonly bool _rejectInboundCalls;

    public RpcPeerResponseBuilder(
        ISerializer serializer,
        InstanceRegistry registry,
        IReadOnlyDictionary<string, IServiceDispatcher> dispatchers,
        bool rejectInboundCalls,
        Func<Exception, RpcErrorInfo?>? exceptionTransformer = null)
    {
        _inner = new RpcDispatchResponseBuilder(serializer, dispatchers, exceptionTransformer);
        _registry = registry;
        _rejectInboundCalls = rejectInboundCalls;
    }

    public void FreezeDispatchers() => _inner.FreezeDispatchers();

    public bool RequiresStreamingContext(RpcRequest request) =>
        !_rejectInboundCalls && _inner.RequiresStreamingContext(request);

    public IServiceDispatcher? ResolveDispatcher(
        RpcRequest request,
        out bool requiresStreamingContext)
    {
        requiresStreamingContext = false;
        if (_rejectInboundCalls || !_inner.TryResolveDispatcher(request, out var dispatcher))
        {
            return null;
        }

        requiresStreamingContext = dispatcher is not INonStreamingServiceDispatcher;
        return dispatcher;
    }

    public ValueTask<RpcDispatchResult> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        RpcStreamingContext streaming,
        CancellationToken ct)
    {
        var dispatcher = ResolveDispatcher(request, out _);
        return BuildAsync(request, messageId, payload, streaming, dispatcher, ct);
    }

    public ValueTask<RpcDispatchResult> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        RpcStreamingContext streaming,
        IServiceDispatcher? dispatcher,
        CancellationToken ct)
    {
        if (_rejectInboundCalls)
        {
            return new ValueTask<RpcDispatchResult>(new RpcDispatchResult(
                _inner.BuildErrorFrame(
                    messageId,
                    new RpcError("This peer does not accept inbound calls.", RpcErrorTypes.InboundRejected)),
                stream: null));
        }

        return _inner.BuildAsync(request, messageId, payload, _registry, streaming, dispatcher, ct);
    }

    public Payload BuildProtocolErrorFrame(int messageId, string errorMessage) =>
        _inner.BuildProtocolErrorFrame(messageId, errorMessage);

    public Payload BuildErrorFrame(int messageId, RpcError error) =>
        _inner.BuildErrorFrame(messageId, error);
}
