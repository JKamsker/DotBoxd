namespace ShaRPC.Core.Transport;

/// <summary>
/// Controls what a bounded ShaRPC queue does when it is full.
/// </summary>
public enum ShaRpcQueueFullMode
{
    /// <summary>
    /// Waits for queue space instead of dropping the incoming frame.
    /// </summary>
    Wait = 0,

    /// <summary>
    /// Drops the incoming frame when the target queue is full.
    /// </summary>
    DropIncoming = 1,
}
