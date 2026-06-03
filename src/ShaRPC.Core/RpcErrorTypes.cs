namespace ShaRPC.Core;

/// <summary>
/// ShaRPC-defined remote error type names.
/// </summary>
public static class RpcErrorTypes
{
    /// <summary>Remote type name used when an internal service failure is hidden from the caller.</summary>
    public const string InternalError = "ShaRpcInternalError";

    /// <summary>Remote type name used when a peer rejects all inbound calls by configuration.</summary>
    public const string InboundRejected = "ShaRpcInboundRejected";

    /// <summary>Remote type name used when the requested service is not registered.</summary>
    public const string ServiceNotFound = "ShaRpcServiceNotFound";

    /// <summary>Remote type name used when the requested method is not found on the service.</summary>
    public const string MethodNotFound = "ShaRpcMethodNotFound";

    /// <summary>Remote type name used when a sub-service instance is missing or has expired.</summary>
    public const string InstanceNotFound = "ShaRpcInstanceNotFound";

    /// <summary>Remote type name used when the peer drops an inbound call because its queue is full.</summary>
    public const string QueueFull = "ShaRpcQueueFull";

    /// <summary>Remote type name used when a protocol-level error is detected.</summary>
    public const string ProtocolError = "ShaRpcProtocolError";
}
