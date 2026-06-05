namespace ShaRPC.Core.Protocol;

/// <summary>
/// Defines the types of messages in the ShaRPC protocol.
/// </summary>
public enum MessageType : byte
{
    /// <summary>
    /// An RPC request from client to server.
    /// </summary>
    Request = 0x01,

    /// <summary>
    /// A successful response from server to client.
    /// </summary>
    Response = 0x02,

    /// <summary>
    /// An error response from server to client.
    /// </summary>
    Error = 0x03,

    /// <summary>
    /// A cancellation request for an in-flight RPC request.
    /// </summary>
    Cancel = 0x04,

    /// <summary>
    /// A chunk belonging to a multiplexed streaming payload.
    /// </summary>
    StreamItem = 0x05,

    /// <summary>
    /// End-of-stream marker for a multiplexed streaming payload.
    /// </summary>
    StreamComplete = 0x06,

    /// <summary>
    /// Error marker for a multiplexed streaming payload.
    /// </summary>
    StreamError = 0x07,

    /// <summary>
    /// Receiver-to-sender flow-control credit for a multiplexed stream.
    /// </summary>
    StreamCredit = 0x08,

    /// <summary>
    /// Cancellation request for a multiplexed stream, distinct from RPC request cancellation.
    /// </summary>
    StreamCancel = 0x09
}
