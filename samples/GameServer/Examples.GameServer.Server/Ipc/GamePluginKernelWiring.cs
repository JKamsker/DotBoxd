using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Kernels.Game.Server.Simulation;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal sealed class GamePluginKernelWiring
{
    private readonly PluginServer _server;
    private readonly GameWorld _world;
    private readonly IPluginEventCallback? _eventCallback;

    public GamePluginKernelWiring(
        PluginServer server,
        GameWorld world,
        IPluginEventCallback? eventCallback)
    {
        _server = server;
        _world = world;
        _eventCallback = eventCallback;
    }

    public void ValidateSupportedEvent(PluginPackage package)
    {
        var subscription = SubscribedEvent(package.Manifest);
        if (SimpleEventName(subscription) is "MonsterAggroEvent" or "AttackEvent" or "RemoteDamageDecisionEvent")
        {
            return;
        }

        throw new InvalidOperationException(
            $"Plugin '{package.Manifest.PluginId}' subscribes to unsupported event '{subscription}'.");
    }

    public void WireHook(InstalledKernel kernel)
    {
        // Map by the kernel's declared event so the server stays agnostic of plugin ids. Manifests now
        // carry the fully-qualified event name; match on the simple-name tail so qualified and legacy
        // simple-name manifests both wire correctly.
        var subscription = SubscribedEvent(kernel.Manifest);
        switch (SimpleEventName(subscription))
        {
            case "MonsterAggroEvent":
                WireHookFor<MonsterAggroEvent>(kernel);
                break;
            case "AttackEvent":
                WireHookFor<AttackEvent>(kernel);
                break;
            case "RemoteDamageDecisionEvent":
                WireHookFor<RemoteDamageDecisionEvent>(kernel);
                break;
            default:
                throw new InvalidOperationException(
                    $"Plugin '{kernel.Manifest.PluginId}' subscribes to unsupported event '{subscription}'.");
        }
    }

    public void WireSubscription(InstalledKernel kernel)
    {
        var subscription = SubscribedEvent(kernel.Manifest);
        switch (SimpleEventName(subscription))
        {
            case "MonsterAggroEvent":
                WireSubscriptionFor<MonsterAggroEvent>(kernel);
                break;
            case "AttackEvent":
                WireSubscriptionFor<AttackEvent>(kernel);
                break;
            default:
                throw new InvalidOperationException(
                    $"Plugin '{kernel.Manifest.PluginId}' subscribes to unsupported event '{subscription}'.");
        }
    }

    // A local-terminal (RunLocal) chain projects server-side and pushes the projected value back to the
    // plugin's native delegate; an ordinary chain runs entirely server-side via Use(kernel).
    private void WireHookFor<TEvent>(InstalledKernel kernel)
    {
        var pipeline = _server.Hooks.On<TEvent>();
        if (IsResultHook(kernel.Manifest))
        {
            var priority = Priority(kernel.Manifest);
            var resultType = HookResultTypeFor<TEvent>(kernel.Manifest);
            if (IsResultLocalTerminal(kernel.Manifest))
            {
                pipeline.UseProjectingResult(
                    kernel,
                    kernel.Manifest.PluginId,
                    resultType,
                    LocalResultRequest(),
                    priority);
            }
            else
            {
                pipeline.UseResult(kernel, resultType, priority);
            }
        }
        else if (IsLocalTerminal(kernel.Manifest))
        {
            pipeline.UseProjecting(kernel, kernel.Manifest.PluginId, LocalPush());
        }
        else
        {
            pipeline.Use(kernel);
        }
    }

    private void WireSubscriptionFor<TEvent>(InstalledKernel kernel)
    {
        // A local-terminal (RunLocal) chain must keep the projection on the broad pipeline so the host can
        // capture the projected value and push it back; index routing would run the kernel and discard it.
        if (IsLocalTerminal(kernel.Manifest))
        {
            _server.Subscriptions.On<TEvent>().UseProjecting(kernel, kernel.Manifest.PluginId, LocalPush());
            return;
        }

        if (!TryRouteThroughIndex<TEvent>(kernel))
        {
            _server.Subscriptions.On<TEvent>().Use(kernel);
        }
    }

    private RemoteLocalPush LocalPush()
    {
        var callback = _eventCallback ?? throw new InvalidOperationException(
            "the connected plugin did not provide an IPluginEventCallback; a remote RunLocal chain requires it.");
        return (subscriptionId, projectedValue, ct) => callback.OnEventAsync(subscriptionId, projectedValue, ct);
    }

    private RemoteLocalResultRequest LocalResultRequest()
    {
        var callback = _eventCallback ?? throw new InvalidOperationException(
            "the connected plugin did not provide an IPluginEventCallback; a remote RegisterLocal chain requires it.");
        return (subscriptionId, contextValue, ct) => callback.OnResultAsync(subscriptionId, contextValue, ct);
    }

    // Issue #49: indexed subscriptions are dispatched through the world's EventIndexRegistry so events are
    // prefiltered before verified IR runs. Returns false when no usable index metadata is present.
    private bool TryRouteThroughIndex<TEvent>(InstalledKernel kernel)
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

        var adapter = _server.Events.Resolve<TEvent>();
        return _world.IndexRegistry.Register(
            adapter,
            kernel,
            subscription.IndexedPredicates,
            subscription.IndexCoversPredicate);
    }

    private static bool IsLocalTerminal(PluginManifest manifest)
        => manifest.Subscriptions.Count > 0 && manifest.Subscriptions[0].LocalTerminal;

    private static bool IsResultHook(PluginManifest manifest)
        => manifest.Subscriptions.Count > 0 && manifest.Subscriptions[0].ResultType is not null;

    private static bool IsResultLocalTerminal(PluginManifest manifest)
        => manifest.Subscriptions.Count > 0 && manifest.Subscriptions[0].ResultLocalTerminal;

    private static int Priority(PluginManifest manifest)
        => manifest.Subscriptions.Count > 0 ? manifest.Subscriptions[0].Priority : 0;

    private static Type HookResultTypeFor<TEvent>(PluginManifest manifest)
    {
        var hook = (DotBoxD.Abstractions.HookAttribute?)Attribute.GetCustomAttribute(
            typeof(TEvent),
            typeof(DotBoxD.Abstractions.HookAttribute),
            inherit: false);
        return hook?.ResultType ?? throw new InvalidOperationException(
            $"Plugin '{manifest.PluginId}' installed a result hook for '{typeof(TEvent).FullName}', but the event type has no [Hook] result declaration.");
    }

    private static string? SubscribedEvent(PluginManifest manifest)
        => manifest.Subscriptions.Count > 0 ? manifest.Subscriptions[0].Event : null;

    private static string? SimpleEventName(string? eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return eventName;
        }

        var lastDot = eventName!.LastIndexOf('.');
        return lastDot >= 0 && lastDot < eventName.Length - 1
            ? eventName[(lastDot + 1)..]
            : eventName;
    }
}
