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
    public const int DefaultMaxConcurrentInboundDispatch = 1;

    /// <summary>Default <see cref="MaxInboundBytes"/>: 64 MiB of in-flight inbound frames per peer.</summary>
    public const long DefaultMaxInboundBytes = 64L * 1024 * 1024;

    private int? _inboundQueueCapacity = DefaultInboundQueueCapacity;
    private int _maxPendingRequests = DefaultMaxPendingRequests;
    private int _maxConcurrentInboundDispatch = DefaultMaxConcurrentInboundDispatch;
    private long? _maxInboundBytes = DefaultMaxInboundBytes;
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
    /// Optional hook, invoked on the side that runs the service, that turns an exception thrown by a
    /// handler into the error returned to the caller. Return an <see cref="RpcErrorInfo"/> to surface
    /// that message and type to the caller, or <see langword="null"/> to keep the default opaque
    /// "Internal error." response for that exception.
    /// <para>
    /// When this is <see langword="null"/> (the default) every handler exception is reported opaquely
    /// so internal failure detail is never leaked. Framework protocol errors (service, method, and
    /// instance not found) keep their own typed mapping and are not routed through this hook.
    /// </para>
    /// <para>
    /// Exposing exception detail can leak sensitive information, so this is opt-in — only enable it
    /// for trusted peers, or map exceptions to safe, caller-facing messages. The shortcut
    /// <c>ExceptionTransformer = ex =&gt; RpcErrorInfo.FromException(ex)</c> exposes every exception's
    /// message and type. The hook may be invoked concurrently when
    /// <see cref="MaxConcurrentInboundDispatch"/> is greater than one, so keep it thread-safe.
    /// </para>
    /// </summary>
    public Func<Exception, RpcErrorInfo?>? ExceptionTransformer { get; init; }

    /// <summary>
    /// When <see langword="true"/>, inbound request frames are answered with an explicit
    /// "this peer does not accept inbound calls" error rather than a "service not found"
    /// error. Use it to make a get-only ("client") peer's one-directional intent explicit.
    /// This is not an authentication or authorization boundary.
    /// </summary>
    public bool RejectInboundCalls { get; init; }

    /// <summary>
    /// When <see langword="true"/>, non-streaming inbound calls do not allocate per-request
    /// cancellation state. The handler receives <see cref="CancellationToken.None"/> and inbound
    /// Cancel frames for those calls are ignored. Streaming calls still allocate cancellation state so
    /// stream cleanup and response-stream teardown remain cancellable.
    /// </summary>
    /// <remarks>
    /// This is a low-allocation option for trusted peers and handlers whose work is short or bounded
    /// elsewhere. Peer shutdown waits for those handlers instead of interrupting them. Leave it
    /// disabled when callers must be able to stop in-flight handlers with a Cancel frame or when
    /// handlers depend on the supplied cancellation token.
    /// </remarks>
    public bool DisableInboundRequestCancellation { get; init; }

    /// <summary>
    /// When <see langword="true"/>, generated generic <see cref="ValueTask{TResult}"/> unary proxy
    /// calls may use a pooled response source instead of the default <see cref="Task{TResult}"/> path.
    /// </summary>
    /// <remarks>
    /// The optimized path is only used when <see cref="RequestTimeout"/> is
    /// <see cref="Timeout.InfiniteTimeSpan"/> and the caller does not pass a cancellable token. It can
    /// run continuations inline on the peer read loop and follows the normal <see cref="ValueTask{TResult}"/>
    /// single-consumption rules. Leave this disabled unless the peer is on a measured, trusted hot path
    /// and every returned <see cref="ValueTask{TResult}"/> is awaited exactly once.
    /// </remarks>
    public bool EnableLowAllocationValueTaskInvocations { get; init; }

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

    /// <summary>
    /// Maximum number of inbound requests dispatched concurrently when <see cref="InboundQueueCapacity"/>
    /// is set. The default of 1 dispatches serially per connection (preserving ordering and bounding
    /// work). Raise it for concurrent per-connection dispatch; total in-flight inbound work is then
    /// bounded by <see cref="InboundQueueCapacity"/> + this value. Ignored when
    /// <see cref="InboundQueueCapacity"/> is <see langword="null"/> (which dispatches immediately and
    /// does not cap concurrency).
    /// </summary>
    public int MaxConcurrentInboundDispatch
    {
        get => _maxConcurrentInboundDispatch;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxConcurrentInboundDispatch),
                    value,
                    "Maximum concurrent inbound dispatch must be greater than zero.");
            }

            _maxConcurrentInboundDispatch = value;
        }
    }

    /// <summary>
    /// Maximum total bytes of in-flight inbound request frames buffered for dispatch when
    /// <see cref="InboundQueueCapacity"/> is set. <see cref="InboundQueueCapacity"/> bounds frame
    /// <em>count</em> only, so it alone permits up to capacity × max-frame-size bytes (a hostile or
    /// overwhelming peer sending large frames can otherwise pin that much memory). This caps the peak
    /// independent of frame size; the default is 64 MiB. A frame larger than the budget is still
    /// admitted when no other inbound work is in flight, so one oversized request never deadlocks.
    /// Set to <see langword="null"/> to disable the byte bound (count-only). Ignored when
    /// <see cref="InboundQueueCapacity"/> is <see langword="null"/> (which does not buffer or bound).
    /// </summary>
    public long? MaxInboundBytes
    {
        get => _maxInboundBytes;
        init
        {
            if (value is <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxInboundBytes),
                    value,
                    "Maximum inbound bytes must be greater than zero.");
            }

            _maxInboundBytes = value;
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
