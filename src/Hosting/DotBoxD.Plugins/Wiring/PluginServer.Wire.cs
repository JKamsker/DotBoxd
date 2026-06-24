using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins;

public sealed partial class PluginServer
{
    private static readonly WireOptions DefaultWireOptions = new();

    /// <summary>
    /// Wires an installed <paramref name="kernel"/> into the hook pipeline for the event it subscribes to,
    /// selecting the terminal (<c>Use</c> / <c>UseProjecting</c> / <c>UseResult</c> / <c>UseProjectingResult</c>)
    /// from the kernel's trusted, recomputed classification. Replaces the per-event / per-terminal routing every
    /// host used to hand-write. The event is resolved by name from the registered adapters, so the host stays
    /// agnostic of plugin ids and event types. Throws when the subscribed event has no registered adapter.
    /// </summary>
    public void WireHook(InstalledKernel kernel, WireOptions? options = null)
    {
        var opts = options ?? DefaultWireOptions;
        var (erased, terminal, callbacks) = Prepare(kernel, opts);
        erased.WireHook(Hooks, kernel, terminal, callbacks);
    }

    /// <summary>
    /// Wires an installed <paramref name="kernel"/> into the subscription (fire-and-forget) pipeline for the
    /// event it subscribes to. A plain terminal is routed through <see cref="WireOptions.IndexRegistry"/> first
    /// (when supplied, <see cref="WireOptions.UseIndex"/> is set, and the subscription carries index metadata),
    /// falling back to the broad pipeline; a projecting terminal pushes to the plugin's native delegate. Result
    /// terminals are rejected — subscriptions have no result channel.
    /// </summary>
    public void WireSubscription(InstalledKernel kernel, WireOptions? options = null)
    {
        var opts = options ?? DefaultWireOptions;
        var (erased, terminal, callbacks) = Prepare(kernel, opts);
        var indexRegistry = opts.UseIndex ? opts.IndexRegistry : null;
        erased.WireSubscription(Subscriptions, kernel, terminal, callbacks, indexRegistry);
    }

    private (IErasedPluginEventAdapter Erased, KernelWireTerminal Terminal, WireCallbacks Callbacks) Prepare(
        InstalledKernel kernel,
        WireOptions options)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();

        var eventName = kernel.Manifest.Subscriptions.Count > 0
            ? kernel.Manifest.Subscriptions[0].Event
            : null;
        if (eventName is null || !Events.TryResolveErased(eventName, out var erased))
        {
            throw new InvalidOperationException(
                $"Plugin '{kernel.Manifest.PluginId}' subscribes to unsupported event '{eventName}'.");
        }

        var terminal = KernelWireTerminal.Classify(kernel, erased.HookResultType);
        if (options.ClassifyOverride is not null)
        {
            terminal = options.ClassifyOverride(terminal);
        }

        return (erased, terminal, new WireCallbacks(options.LocalPush, options.LocalResult));
    }
}
