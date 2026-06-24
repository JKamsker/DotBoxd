using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Kernels.Game.Server.Simulation;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// The host's wiring <b>policy</b> for one plugin connection. It declares which events this server supports
/// (registering their adapters so the framework router can resolve a kernel's subscribed event by name),
/// validates a package's route, and hands installed kernels to <see cref="PluginServer.WireHook"/> /
/// <see cref="PluginServer.WireSubscription"/> with host-specific <see cref="WireOptions"/>: the remote-local
/// callbacks derived from the connection's <see cref="IPluginEventCallback"/>, and the world-owned event index.
/// <para>
/// All the per-event / per-terminal routing that used to live here (the event switch, terminal classification,
/// projection-push wrappers, and index routing) now lives in the framework router — this file is host policy,
/// not plumbing.
/// </para>
/// </summary>
internal sealed class GamePluginKernelWiring
{
    private readonly PluginServer _server;
    private readonly IPluginEventCallback? _eventCallback;
    private readonly WireOptions _hookOptions;
    private readonly WireOptions _subscriptionOptions;

    public GamePluginKernelWiring(
        PluginServer server,
        GameWorld world,
        IPluginEventCallback? eventCallback)
    {
        _server = server;
        _eventCallback = eventCallback;

        // Declare the events this host supports by registering their adapters, so the framework router can
        // resolve a kernel's subscribed event BY NAME at wire time. The router does not auto-register — this is
        // the host's explicit event surface, and exactly the set ValidateSupportedEvent allows below.
        server.Events.Resolve<MonsterAggroEvent>();
        server.Events.Resolve<AttackEvent>();
        server.Events.Resolve<RemoteDamageDecisionEvent>();

        RemoteLocalPush? push = eventCallback is null
            ? null
            : (subscriptionId, projectedValue, ct) => eventCallback.OnEventAsync(subscriptionId, projectedValue, ct);
        RemoteLocalResultRequest? result = eventCallback is null
            ? null
            : (subscriptionId, contextValue, ct) => eventCallback.OnResultAsync(subscriptionId, contextValue, ct);

        _hookOptions = new WireOptions { LocalPush = push, LocalResult = result };
        _subscriptionOptions = new WireOptions
        {
            LocalPush = push,
            LocalResult = result,
            IndexRegistry = world.IndexRegistry
        };
    }

    public void ValidateSupportedEvent(PluginPackage package)
    {
        var subscription = SubscribedEvent(package.Manifest);
        if (MatchesEvent<MonsterAggroEvent>(subscription) ||
            MatchesEvent<AttackEvent>(subscription) ||
            MatchesEvent<RemoteDamageDecisionEvent>(subscription))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Plugin '{package.Manifest.PluginId}' subscribes to unsupported event '{subscription}'.");
    }

    public void ValidateRoute(PluginPackage package)
    {
        ValidateSupportedEvent(package);
        if (!RequiresLocalCallback(package.Manifest) || _eventCallback is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Plugin '{package.Manifest.PluginId}' requires remote local-terminal callback routing, but the connected "
            + "plugin did not provide an IPluginEventCallback.");
    }

    // The framework router classifies the verified terminal and selects Use / UseProjecting / UseResult /
    // UseProjectingResult; the host only supplies the callbacks and (for subscriptions) the world index.
    public void WireHook(InstalledKernel kernel) => _server.WireHook(kernel, _hookOptions);

    public void WireSubscription(InstalledKernel kernel) => _server.WireSubscription(kernel, _subscriptionOptions);

    private static bool RequiresLocalCallback(PluginManifest manifest)
        => manifest.Subscriptions.Count > 0 &&
           (manifest.Subscriptions[0].LocalTerminal || manifest.Subscriptions[0].ResultLocalTerminal);

    private static string? SubscribedEvent(PluginManifest manifest)
        => manifest.Subscriptions.Count > 0 ? manifest.Subscriptions[0].Event : null;

    private static bool MatchesEvent<TEvent>(string? eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return false;
        }

        if (string.Equals(eventName, typeof(TEvent).FullName, StringComparison.Ordinal) ||
            string.Equals(SimpleEventName(eventName), typeof(TEvent).Name, StringComparison.Ordinal))
        {
            return true;
        }

        var hook = (HookAttribute?)Attribute.GetCustomAttribute(
            typeof(TEvent),
            typeof(HookAttribute),
            inherit: false);
        return hook is not null &&
            string.Equals(eventName, hook.Name, StringComparison.Ordinal);
    }

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
