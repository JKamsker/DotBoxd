using DotBoxD.Abstractions;
using DotBoxD.Kernels;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Subscription-surface analogue of <see cref="RunLocalHarness{TEvent}"/>. It wires the same live
/// <see cref="PluginServer"/>, message sink, and local-handler registry, but drives the chain through the
/// <see cref="RemoteSubscriptionRegistry"/> / <c>server.Subscriptions</c> path — a distinct interception target from
/// the hooks surface. Subscription publish is fire-and-forget (void), so tests gate delivery on a
/// <see cref="System.Threading.Tasks.TaskCompletionSource"/> the native terminal signals.
/// </summary>
internal sealed class SubscriptionHarness<TEvent> : IDisposable
{
    public InMemoryPluginMessageSink Sink { get; } = new();

    public PluginServer Server { get; }

    public RemoteLocalHandlerRegistry LocalHandlers { get; } = new();

    public RemoteSubscriptionRegistry Subscriptions { get; }

    public SubscriptionHarness(SandboxPolicy? policy = null)
    {
        Server = PluginServer.Create(Sink, defaultPolicy: policy ?? TestPolicies.Chain());

        var server = Server;
        var sink = Sink;
        var localHandlers = LocalHandlers;
        Subscriptions = new RemoteSubscriptionRegistry(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                var subscriptionId = package.Manifest.PluginId;
                server.Subscriptions.On<TEvent>().UseProjecting(
                    kernel,
                    subscriptionId,
                    (id, payload, token) =>
                        localHandlers.DispatchAsync(id, payload.ToArray(), new HookContext(sink, token)));
                return subscriptionId;
            },
            localHandlers);
    }

    /// <summary>Fire-and-forget subscription publish; gate delivery on a signal from the native terminal.</summary>
    public void Publish(TEvent e) => Server.Subscriptions.Publish(e);

    public void Dispose() => Server.Dispose();
}
