using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

/// <summary>
/// Options for <see cref="RpcPeer"/> and <see cref="RpcHost"/>.
/// </summary>
public sealed record RpcPeerOptions
{
    /// <summary>Default per-call timeout for proxies created by this peer.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

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
    /// Maximum queued inbound requests. Null dispatches inbound requests immediately and does not
    /// cap concurrent dispatch work. In wait mode, response and cancel frames keep being read while
    /// request admission waits for dispatch queue space.
    /// </summary>
    public int? InboundQueueCapacity { get; init; }

    /// <summary>Policy used when <see cref="InboundQueueCapacity"/> is set and the request queue is full.</summary>
    public ShaRpcQueueFullMode QueueFullMode { get; init; } = ShaRpcQueueFullMode.Wait;
}
