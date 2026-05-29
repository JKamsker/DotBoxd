namespace ShaRPC.Core.Protocol;

/// <summary>
/// Represents an RPC response message.
/// </summary>
public sealed class RpcResponse
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
}
