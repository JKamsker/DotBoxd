using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core;

internal sealed partial class RpcPeerOutboundInvoker
{
    private static void ValidateTarget(string service, string method)
    {
        if (string.IsNullOrEmpty(service))
        {
            throw new ArgumentException("Service name must not be null or empty.", nameof(service));
        }

        if (string.IsNullOrEmpty(method))
        {
            throw new ArgumentException("Method name must not be null or empty.", nameof(method));
        }
    }

    private static RpcRequest CreateEnvelope(
        int messageId,
        string service,
        string method,
        string? instanceId,
        RpcStreamAttachment[]? streams) =>
        new()
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            InstanceId = instanceId,
            Streams = CreateHandles(streams),
        };

    private static RpcStreamHandle[]? CreateHandles(RpcStreamAttachment[]? streams)
    {
        if (streams is null || streams.Length == 0)
        {
            return null;
        }

        var handles = new RpcStreamHandle[streams.Length];
        for (var i = 0; i < streams.Length; i++)
        {
            handles[i] = streams[i].Handle;
        }

        return handles;
    }
}
