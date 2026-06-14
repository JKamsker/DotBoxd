namespace ShaRPC.Core;

/// <summary>
/// Describes an accepted <see cref="RpcPeer"/> connection.
/// </summary>
public sealed class RpcPeerEventArgs : EventArgs
{
    public RpcPeerEventArgs(RpcPeer peer)
    {
        Peer = peer;
    }

    /// <summary>The peer associated with the host event.</summary>
    public RpcPeer Peer { get; }
}
