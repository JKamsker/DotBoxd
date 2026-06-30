using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Client;

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

    private static string ValidateInstanceId(string instanceId)
    {
        if (instanceId is null)
        {
            throw new ArgumentNullException(nameof(instanceId));
        }

        return instanceId;
    }

    private static Task MissingInstanceIdTask() =>
        Task.FromException(new ArgumentNullException("instanceId"));

    private static Task<T> MissingInstanceIdTask<T>() =>
        Task.FromException<T>(new ArgumentNullException("instanceId"));

    private static ValueTask MissingInstanceIdValueTask() =>
        new(MissingInstanceIdTask());

    private static ValueTask<T> MissingInstanceIdValueTask<T>() =>
        new(MissingInstanceIdTask<T>());

    private static RpcRequest CreateEnvelope(
        int messageId,
        string service,
        string method,
        string? instanceId,
        RpcStreamAttachment[]? streams) =>
        CreateEnvelope(messageId, service, method, instanceId, RpcStreamAttachmentSet.FromArray(streams));

    private static RpcRequest CreateEnvelope(
        int messageId,
        string service,
        string method,
        string? instanceId,
        RpcStreamAttachmentSet streams) =>
        new()
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            InstanceId = instanceId,
            Streams = CreateHandles(streams),
        };

    private static RpcStreamHandle[]? CreateHandles(RpcStreamAttachmentSet streams)
    {
        if (streams.IsEmpty)
        {
            return null;
        }

        var handles = new RpcStreamHandle[streams.Count];
        for (var i = 0; i < handles.Length; i++)
        {
            handles[i] = streams.GetAt(i)!.Handle;
        }

        return handles;
    }
}
