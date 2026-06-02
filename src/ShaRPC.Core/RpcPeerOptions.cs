using System.Threading;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

/// <summary>
/// Options for <see cref="RpcPeer"/> and <see cref="RpcHost"/>.
/// </summary>
public sealed class RpcPeerOptions
{
    public const int DefaultInboundQueueCapacity = 1024;
    public const int DefaultMaxPendingRequests = 4096;

    private int? _inboundQueueCapacity = DefaultInboundQueueCapacity;
    private int _maxPendingRequests = DefaultMaxPendingRequests;
    private ShaRpcQueueFullMode _queueFullMode = ShaRpcQueueFullMode.Wait;
    private TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default per-call timeout for proxies created by this peer. Must be a positive
    /// <see cref="TimeSpan"/> (at most <see cref="int.MaxValue"/> milliseconds) or
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable the timeout.
    /// </summary>
    public TimeSpan RequestTimeout
    {
        get => _requestTimeout;
        init
        {
            if (value != Timeout.InfiniteTimeSpan &&
                (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(RequestTimeout),
                    value,
                    "Request timeout must be positive (at most int.MaxValue ms) or Timeout.InfiniteTimeSpan.");
            }

            _requestTimeout = value;
        }
    }

    /// <summary>
    /// Optional service provider for dispatcher factories that resolve dependencies.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; init; }

    /// <summary>
    /// When <see langword="true"/>, inbound request frames are answered with an explicit
    /// "this peer does not accept inbound calls" error rather than a "service not found"
    /// error. Use it to make a get-only ("client") peer's one-directional intent explicit.
    /// This is not an authentication or authorization boundary.
    /// </summary>
    public bool RejectInboundCalls { get; init; }

    /// <summary>
    /// Maximum queued inbound requests. The default applies bounded read-side backpressure. Null
    /// dispatches inbound requests immediately, does not cap concurrent dispatch work, and should
    /// only be used with trusted peers or externally bounded transports. In wait mode, request
    /// admission waits for bounded dispatch queue space.
    /// </summary>
    /// <remarks>
    /// A peer demuxes responses, cancels, and inbound requests over a single read loop. In
    /// <see cref="ShaRpcQueueFullMode.Wait"/> mode that loop parks when the request queue is full,
    /// which also pauses reading responses to this peer's own outbound calls. For a bidirectional
    /// peer whose inbound handlers call back into the same peer, size this capacity above the
    /// maximum number of inbound requests that can arrive ahead of those callbacks' responses, or
    /// use <see langword="null"/> (unbounded) or <see cref="ShaRpcQueueFullMode.DropIncoming"/>;
    /// otherwise an under-sized Wait queue can stall a reentrant response until
    /// <see cref="RequestTimeout"/>.
    /// </remarks>
    public int? InboundQueueCapacity
    {
        get => _inboundQueueCapacity;
        init
        {
            if (value is <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(InboundQueueCapacity),
                    value,
                    "Inbound queue capacity must be greater than zero.");
            }

            _inboundQueueCapacity = value;
        }
    }

    /// <summary>Maximum concurrent outbound calls waiting for responses.</summary>
    public int MaxPendingRequests
    {
        get => _maxPendingRequests;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxPendingRequests),
                    value,
                    "Maximum pending requests must be greater than zero.");
            }

            _maxPendingRequests = value;
        }
    }

    /// <summary>Policy used when <see cref="InboundQueueCapacity"/> is set and the request queue is full.</summary>
    public ShaRpcQueueFullMode QueueFullMode
    {
        get => _queueFullMode;
        init
        {
            if (!Enum.IsDefined(typeof(ShaRpcQueueFullMode), value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(QueueFullMode),
                    value,
                    "Unknown queue full mode.");
            }

            _queueFullMode = value;
        }
    }
}
