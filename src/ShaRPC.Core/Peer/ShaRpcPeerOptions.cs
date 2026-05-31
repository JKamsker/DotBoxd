using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Peer;

/// <summary>
/// Options for <see cref="ShaRpcPeer"/>.
/// </summary>
public sealed class ShaRpcPeerOptions
{
    /// <summary>
    /// Default request timeout for proxies created by the peer. Null uses the client default.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>
    /// Maximum queued frames per peer direction. Null uses unbounded queues.
    /// </summary>
    public int? InboundQueueCapacity { get; set; }

    /// <summary>
    /// Policy used when <see cref="InboundQueueCapacity"/> is set and a direction queue is full.
    /// </summary>
    public ShaRpcQueueFullMode QueueFullMode { get; set; } = ShaRpcQueueFullMode.Wait;

    internal DuplexConnectionSplitterOptions ToSplitterOptions() =>
        new()
        {
            QueueCapacity = InboundQueueCapacity,
            QueueFullMode = QueueFullMode,
        };
}
