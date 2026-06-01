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

    /// <summary>Remote type name used when a protocol-level error is detected.</summary>
    public const string ProtocolError = "ShaRpcProtocolError";
}
