namespace ShaRPC.Core.Transport;

/// <summary>
/// Controls what a duplex splitter does when a bounded inbound frame queue is full.
/// </summary>
public enum ShaRpcQueueFullMode
{
    /// <summary>
    /// Waits for queue space before reading more frames from the shared connection.
    /// </summary>
    Wait = 0,

    /// <summary>
    /// Drops the incoming frame when the target queue is full.
    /// </summary>
    DropIncoming = 1,
}
