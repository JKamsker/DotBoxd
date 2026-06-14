namespace DotBoxd.Services.Transport;

/// <summary>
/// Controls what a bounded DotBoxd queue does when it is full.
/// </summary>
public enum DotBoxdRpcQueueFullMode
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
