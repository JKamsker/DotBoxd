using DotBoxD.Plugins.Indexing;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins;

/// <summary>
/// The remote-callback delegates a projecting terminal needs: <see cref="LocalPush"/> for a remote
/// <c>RunLocal</c> chain and <see cref="LocalResult"/> for a remote <c>RegisterLocal</c> chain. Either may be
/// <c>null</c> for a connection that uses no remote-local terminal; the router only requires the one a kernel's
/// classified terminal actually uses.
/// </summary>
public readonly record struct WireCallbacks(RemoteLocalPush? LocalPush, RemoteLocalResultRequest? LocalResult);

/// <summary>
/// The resolved event adapter and terminal used when an installed kernel is wired through
/// <see cref="PluginServer.WireHook"/> or <see cref="PluginServer.WireSubscription"/>.
/// </summary>
/// <param name="EventType">The CLR event type selected from the server's registered event adapters.</param>
/// <param name="EventName">The adapter event name that matched the package manifest.</param>
/// <param name="Terminal">The trusted terminal classification used for routing.</param>
public readonly record struct WireResult(Type EventType, string EventName, KernelWireTerminal Terminal);

/// <summary>
/// The host's wiring seam for <see cref="PluginServer.WireHook"/> / <see cref="PluginServer.WireSubscription"/>.
/// Everything mechanical (terminal selection, by-name event resolution, the trusted recompute) is owned by the
/// router; this carries only the genuinely host-specific bits: the remote-local callbacks, the world-owned
/// event index, and an optional classification override.
/// </summary>
public sealed record WireOptions
{
    /// <summary>The remote <c>RunLocal</c> push callback; required when a kernel classifies as a projecting terminal.</summary>
    public RemoteLocalPush? LocalPush { get; init; }

    /// <summary>The remote <c>RegisterLocal</c> result-request callback; required when a kernel classifies as a projecting-result terminal.</summary>
    public RemoteLocalResultRequest? LocalResult { get; init; }

    /// <summary>Whether a plain subscription terminal should attempt index prefiltering through <see cref="IndexRegistry"/>. Defaults to <c>true</c>.</summary>
    public bool UseIndex { get; init; } = true;

    /// <summary>
    /// The host's event index registry (typically world-owned). When supplied and <see cref="UseIndex"/> is set,
    /// a plain subscription whose verified IR maps onto an indexed field is served from the index instead of the
    /// broad pipeline. <c>null</c> (the default) means no index routing — the broad pipeline is used.
    /// </summary>
    public EventIndexRegistry? IndexRegistry { get; init; }

    /// <summary>
    /// Optional post-processing of the trusted <see cref="KernelWireTerminal"/> before routing. A host may
    /// re-route already-verified IR (it cannot widen capability — the verified predicate still runs and the
    /// index remains a prefilter only).
    /// </summary>
    public Func<KernelWireTerminal, KernelWireTerminal>? ClassifyOverride { get; init; }
}
