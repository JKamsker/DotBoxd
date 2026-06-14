namespace ShaRPC.Core.Protocol;

/// <summary>
/// Represents an RPC response message. A <see langword="struct"/> so that framing a response
/// does not heap-allocate the envelope on the server hot path; it is constructed, serialized,
/// and read but never mutated after construction.
/// </summary>
public struct RpcResponse
{
    /// <summary>
    /// The message ID this response corresponds to.
    /// </summary>
    public int MessageId { get; set; }

    /// <summary>
    /// Whether the call was successful.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message (if not successful).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error type name (if not successful).
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// Non-null when the response payload is delivered by subsequent stream frames.
    /// </summary>
    public RpcStreamHandle? Stream { get; set; }
}
