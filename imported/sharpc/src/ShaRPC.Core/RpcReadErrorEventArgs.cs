namespace ShaRPC.Core;

/// <summary>
/// Describes a non-cancellation failure in a <see cref="RpcPeer"/>'s read loop.
/// </summary>
public sealed class RpcReadErrorEventArgs : EventArgs
{
    public RpcReadErrorEventArgs(string remoteEndpoint, Exception error)
    {
        RemoteEndpoint = remoteEndpoint;
        Error = error;
    }

    /// <summary>The remote endpoint string of the channel that failed.</summary>
    public string RemoteEndpoint { get; }

    /// <summary>The read-loop exception.</summary>
    public Exception Error { get; }
}
