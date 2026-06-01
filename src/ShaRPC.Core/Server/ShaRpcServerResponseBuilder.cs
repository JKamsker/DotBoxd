using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Server;

internal sealed class ShaRpcServerResponseBuilder
{
    private readonly RpcDispatchResponseBuilder _inner;

    public ShaRpcServerResponseBuilder(
        ISerializer serializer,
        ConcurrentDictionary<string, IServiceDispatcher> dispatchers)
    {
        _inner = new RpcDispatchResponseBuilder(serializer, dispatchers);
    }

    public async ValueTask<Payload> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        IInstanceRegistry registry,
        CancellationToken ct)
    {
        return await _inner.BuildAsync(request, messageId, payload, registry, ct).ConfigureAwait(false);
    }
}
