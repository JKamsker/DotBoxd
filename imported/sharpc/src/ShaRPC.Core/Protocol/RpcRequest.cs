namespace ShaRPC.Core.Protocol;

/// <summary>
/// Represents an RPC request message. A <see langword="struct"/> so that framing a request
/// does not heap-allocate the envelope on the client hot path; it is constructed, serialized,
/// and read but never mutated after construction.
/// </summary>
public struct RpcRequest
{
    /// <summary>
    /// Initializes an empty request. Required so the string field initializers run for
    /// <c>new RpcRequest()</c> and during deserialization.
    /// </summary>
    public RpcRequest()
    {
    }

    /// <summary>
    /// Unique identifier for this request, used for response correlation.
    /// </summary>
    public int MessageId { get; set; }

    /// <summary>
    /// The name of the service being called.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// The name of the method being called.
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Per-connection opaque identifier of the server-side instance this call targets.
    /// <c>null</c> for ordinary singleton-service calls (the legacy code path); non-null
    /// for nested-service calls dispatched via
    /// <see cref="ShaRPC.Core.Server.IServiceDispatcher.DispatchOnInstanceAsync"/>.
    /// Wire-compatible with older peers because absent properties deserialize to null.
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Streams opened by this request body. The dispatch side registers these handles before
    /// processing later frames so streamed arguments cannot race their receivers.
    /// </summary>
    public RpcStreamHandle[]? Streams { get; set; }
}
