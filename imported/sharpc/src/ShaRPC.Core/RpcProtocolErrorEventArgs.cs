using ShaRPC.Core.Protocol;

namespace ShaRPC.Core;

/// <summary>
/// Describes a malformed or unsupported protocol frame observed by an <see cref="RpcPeer"/>.
/// </summary>
public sealed class RpcProtocolErrorEventArgs : EventArgs
{
    public RpcProtocolErrorEventArgs(
        string remoteEndpoint,
        int messageId,
        MessageType messageType,
        string message)
        : this(remoteEndpoint, messageId, messageType, message, error: null)
    {
    }

    public RpcProtocolErrorEventArgs(
        string remoteEndpoint,
        int messageId,
        MessageType messageType,
        string message,
        Exception? error)
    {
        RemoteEndpoint = remoteEndpoint;
        MessageId = messageId;
        MessageType = messageType;
        Message = message;
        Error = error;
    }

    /// <summary>The remote endpoint string of the channel that sent the frame.</summary>
    public string RemoteEndpoint { get; }

    /// <summary>The message id from the frame header, or 0 when the header was malformed.</summary>
    public int MessageId { get; }

    /// <summary>The message type from the frame header, or default when the header was malformed.</summary>
    public MessageType MessageType { get; }

    /// <summary>A safe diagnostic message describing the protocol problem.</summary>
    public string Message { get; }

    /// <summary>The underlying parse or deserialization error, when one was available.</summary>
    public Exception? Error { get; }
}
