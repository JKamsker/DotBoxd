using DotBoxd.Services.Buffers;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Serialization;
using DotBoxd.Services.Server;
using DotBoxd.Services.Streaming;

namespace DotBoxd.Services;

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

    public ValueTask<RpcDispatchResult> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        RpcStreamingContext streaming,
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

        return _inner.BuildAsync(request, messageId, payload, _registry, streaming, ct);
    }

    public Payload BuildProtocolErrorFrame(int messageId, string errorMessage) =>
        _inner.BuildProtocolErrorFrame(messageId, errorMessage);

    public Payload BuildErrorFrame(int messageId, RpcError error) =>
        _inner.BuildErrorFrame(messageId, error);
}
