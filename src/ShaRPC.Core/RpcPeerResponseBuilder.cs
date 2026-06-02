using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;

namespace ShaRPC.Core;

internal sealed class RpcPeerResponseBuilder
{
    private readonly RpcDispatchResponseBuilder _inner;
    private readonly InstanceRegistry _registry;
    private readonly bool _rejectInboundCalls;

    public RpcPeerResponseBuilder(
        ISerializer serializer,
        InstanceRegistry registry,
        ConcurrentDictionary<string, IServiceDispatcher> dispatchers,
        bool rejectInboundCalls)
    {
        _inner = new RpcDispatchResponseBuilder(serializer, dispatchers);
        _registry = registry;
        _rejectInboundCalls = rejectInboundCalls;
    }

    public async ValueTask<Payload> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        if (_rejectInboundCalls)
        {
            return _inner.BuildErrorFrame(
                messageId,
                new RpcError("This peer does not accept inbound calls.", RpcErrorTypes.InboundRejected));
        }

        return await _inner.BuildAsync(request, messageId, payload, _registry, ct).ConfigureAwait(false);
    }

    public Payload BuildProtocolErrorFrame(int messageId, string errorMessage) =>
        _inner.BuildProtocolErrorFrame(messageId, errorMessage);

    public Payload BuildErrorFrame(int messageId, RpcError error) =>
        _inner.BuildErrorFrame(messageId, error);
}
