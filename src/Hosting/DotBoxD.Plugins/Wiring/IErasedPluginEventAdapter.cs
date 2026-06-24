using DotBoxD.Plugins.Indexing;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// A type-erased, wire-capable view of a registered <see cref="IPluginEventAdapter{TEvent}"/>. The closure is
/// captured once at registration time (when the event type is statically known), so the host-side router
/// (<see cref="PluginServer.WireHook"/> / <see cref="PluginServer.WireSubscription"/>) can wire an installed
/// kernel to the correct typed pipeline terminal <b>by event name</b> — with no reflection at wire time.
/// </summary>
public interface IErasedPluginEventAdapter
{
    /// <summary>The CLR event type this adapter handles.</summary>
    Type EventType { get; }

    /// <summary>The adapter's event name (the <c>[Hook]</c> name or the type name), used for by-name resolution.</summary>
    string EventName { get; }

    /// <summary>The result type declared by the event's <c>[Hook]</c> attribute, or <c>null</c> when it declares none.</summary>
    Type? HookResultType { get; }

    /// <summary>Wires <paramref name="kernel"/> into the hook pipeline for this event using the classified <paramref name="terminal"/>.</summary>
    void WireHook(HookRegistry hooks, InstalledKernel kernel, KernelWireTerminal terminal, WireCallbacks callbacks);

    /// <summary>
    /// Wires <paramref name="kernel"/> into the subscription pipeline for this event. A plain terminal is routed
    /// through <paramref name="indexRegistry"/> first when one is supplied and the subscription carries index
    /// metadata; otherwise it falls back to the broad pipeline. A projecting terminal pushes to the plugin's
    /// native delegate; result terminals are rejected (subscriptions have no result channel).
    /// </summary>
    void WireSubscription(
        SubscriptionRegistry subscriptions,
        InstalledKernel kernel,
        KernelWireTerminal terminal,
        WireCallbacks callbacks,
        EventIndexRegistry? indexRegistry);
}

/// <summary>
/// The concrete generic that closes over the static <typeparamref name="TEvent"/> so the router dispatches to
/// <c>On&lt;TEvent&gt;(adapter)</c> and the typed terminals with no reflection. Holds the <i>same</i> adapter
/// instance the registry stores, so re-resolving the pipeline preserves adapter identity (avoids DBXK034).
/// </summary>
internal sealed class ErasedPluginEventAdapter<TEvent> : IErasedPluginEventAdapter
{
    private readonly IPluginEventAdapter<TEvent> _adapter;

    internal ErasedPluginEventAdapter(IPluginEventAdapter<TEvent> adapter)
    {
        _adapter = adapter;
        HookResultType = ResultTypeOf(typeof(TEvent));
    }

    public Type EventType => typeof(TEvent);

    public string EventName => _adapter.EventName;

    public Type? HookResultType { get; }

    public void WireHook(HookRegistry hooks, InstalledKernel kernel, KernelWireTerminal terminal, WireCallbacks callbacks)
    {
        var pipeline = hooks.On<TEvent>(_adapter);
        switch (terminal.Kind)
        {
            case KernelWireKind.Plain:
                pipeline.Use(kernel);
                break;
            case KernelWireKind.Projecting:
                pipeline.UseProjecting(kernel, RequireCallbackId(terminal, kernel), RequirePush(callbacks));
                break;
            case KernelWireKind.Result:
                pipeline.UseResult(kernel, RequireResultType(terminal, kernel), terminal.Priority);
                break;
            case KernelWireKind.ProjectingResult:
                pipeline.UseProjectingResult(
                    kernel,
                    RequireCallbackId(terminal, kernel),
                    RequireResultType(terminal, kernel),
                    RequireResult(callbacks),
                    terminal.Priority);
                break;
            default:
                throw UnknownKind(terminal.Kind);
        }
    }

    public void WireSubscription(
        SubscriptionRegistry subscriptions,
        InstalledKernel kernel,
        KernelWireTerminal terminal,
        WireCallbacks callbacks,
        EventIndexRegistry? indexRegistry)
    {
        switch (terminal.Kind)
        {
            case KernelWireKind.Plain:
                // A local-terminal projection must stay on the broad pipeline; a plain notification may be
                // served from the index when one is supplied and the verified IR maps onto an indexed field.
                if (indexRegistry is not null && TryRouteThroughIndex(indexRegistry, kernel))
                {
                    return;
                }

                subscriptions.On<TEvent>(_adapter).Use(kernel);
                break;
            case KernelWireKind.Projecting:
                subscriptions.On<TEvent>(_adapter)
                    .UseProjecting(kernel, RequireCallbackId(terminal, kernel), RequirePush(callbacks));
                break;
            default:
                throw new InvalidOperationException(
                    $"Subscriptions support only plain and projecting terminals; kernel '{kernel.Manifest.PluginId}' classified as '{terminal.Kind}'. Wire it as a hook instead.");
        }
    }

    private bool TryRouteThroughIndex(EventIndexRegistry indexRegistry, InstalledKernel kernel)
    {
        if (kernel.Manifest.Subscriptions.Count == 0)
        {
            return false;
        }

        var subscription = kernel.Manifest.Subscriptions[0];
        if (subscription.IndexedPredicates.Count == 0)
        {
            return false;
        }

        // The registry recomputes the matching predicates from verified IR; the manifest predicates passed here
        // are an untrusted hint that merely gates whether to attempt index routing at all.
        return indexRegistry.Register(_adapter, kernel, subscription.IndexedPredicates, subscription.IndexCoversPredicate);
    }

    private static Type? ResultTypeOf(Type eventType)
        => ((HookAttribute?)Attribute.GetCustomAttribute(eventType, typeof(HookAttribute), inherit: false))?.ResultType;

    private static string RequireCallbackId(KernelWireTerminal terminal, InstalledKernel kernel)
        => terminal.CallbackSubscriptionId ?? throw new InvalidOperationException(
            $"Plugin '{kernel.Manifest.PluginId}' requested local-terminal routing without a callback route id.");

    private static Type RequireResultType(KernelWireTerminal terminal, InstalledKernel kernel)
        => terminal.ResultType ?? throw new InvalidOperationException(
            $"Plugin '{kernel.Manifest.PluginId}' classified as a result hook with no result type.");

    private static RemoteLocalPush RequirePush(WireCallbacks callbacks)
        => callbacks.LocalPush ?? throw new InvalidOperationException(
            "the connected plugin did not provide an IPluginEventCallback; a remote RunLocal chain requires it.");

    private static RemoteLocalResultRequest RequireResult(WireCallbacks callbacks)
        => callbacks.LocalResult ?? throw new InvalidOperationException(
            "the connected plugin did not provide an IPluginEventCallback; a remote RegisterLocal chain requires it.");

    private static InvalidOperationException UnknownKind(KernelWireKind kind)
        => new($"Unknown kernel wire kind '{kind}'.");
}
