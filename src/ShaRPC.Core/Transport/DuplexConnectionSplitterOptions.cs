namespace ShaRPC.Core.Transport;

/// <summary>
/// Options for <see cref="DuplexConnectionSplitter"/>.
/// </summary>
public sealed class DuplexConnectionSplitterOptions
{
    /// <summary>
    /// Maximum queued frames per split side. A null value uses unbounded queues.
    /// </summary>
    public int? QueueCapacity { get; set; }

    /// <summary>
    /// Policy used when <see cref="QueueCapacity"/> is set and the target queue is full.
    /// </summary>
    public ShaRpcQueueFullMode QueueFullMode { get; set; } = ShaRpcQueueFullMode.Wait;

    internal DuplexConnectionSplitterOptions CloneAndValidate()
    {
        if (QueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QueueCapacity),
                QueueCapacity,
                "Queue capacity must be positive when it is configured.");
        }

        if (!Enum.IsDefined(typeof(ShaRpcQueueFullMode), QueueFullMode))
        {
            throw new ArgumentOutOfRangeException(nameof(QueueFullMode), QueueFullMode, "Unknown queue full mode.");
        }

        return new DuplexConnectionSplitterOptions
        {
            QueueCapacity = QueueCapacity,
            QueueFullMode = QueueFullMode,
        };
    }
}
