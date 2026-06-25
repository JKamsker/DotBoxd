using DotBoxD.Abstractions;
using DotBoxD.Kernels.Game.Plugin.Authoring;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

/// <summary>
/// Proves the framework router (<see cref="PluginServer.WireHook"/>) selects the SAME terminal the hand-written
/// <c>GamePluginKernelWiring</c> used to pick — without that wiring — for the terminal kinds most at risk of a
/// mis-route: a remote <c>RunLocal</c> projection and a remote <c>RegisterLocal</c> result. Each test drives a
/// real lowered package (authored in the plugin project so the analyzer lowers it) through the router with an
/// in-process callback, and asserts the projecting behavior the manual <c>UseProjecting</c>/
/// <c>UseProjectingResult</c> wiring produces in <c>RemoteRunLocalIpcPremiseTests</c>. Plain hook/subscription
/// routing and rollback are covered by <c>GamePluginControlServiceTests</c>/<c>RollbackTests</c> end-to-end.
/// </summary>
public sealed class RouterParityTests
{
    [Fact]
    public async Task WireHook_routes_a_runlocal_chain_to_the_projecting_terminal()
    {
        var localHandlers = new RemoteLocalHandlerRegistry();
        var calmed = new List<string>();
        var (package, subscriptionId) = LowerCalmReaction(localHandlers, id =>
        {
            lock (calmed)
            {
                calmed.Add(id);
            }
        });

        using var server = PluginServer.Create(defaultPolicy: ProjectionPolicy());
        server.Events.Resolve<MonsterAggroEvent>();
        var kernel = await server.InstallAsync(package);

        var pushes = 0;
        RemoteLocalPush push = async (id, payload, ct) =>
        {
            Interlocked.Increment(ref pushes);
            await localHandlers.DispatchAsync(id, payload, new HookContext(new InMemoryPluginMessageSink(), ct), ct);
        };

        // The router must classify this as Projecting and call UseProjecting (NOT Use): if it ran the kernel
        // server-side via Use, the projection would be discarded and no push would fire.
        server.WireHook(kernel, new WireOptions { LocalPush = push });

        await server.Hooks.PublishAsync(new MonsterAggroEvent("monster-7", "player-1", 3, 8, 1));   // matches Distance <= 4
        await server.Hooks.PublishAsync(new MonsterAggroEvent("monster-9", "player-2", 10, 8, 1));  // filtered server-side

        Assert.Equal(1, pushes);                 // server-side filter ran; exactly one projection pushed
        Assert.Equal(["monster-7"], calmed);     // the projected MonsterId reached the native delegate
        _ = subscriptionId;
    }

    [Fact]
    public async Task WireHook_routes_a_registerlocal_chain_to_the_projecting_result_terminal()
    {
        var localHandlers = new RemoteLocalHandlerRegistry();
        PluginPackage? lowered = null;
        var hooks = new GamePluginHookRegistry(
            package =>
            {
                lowered = package;
                return ValueTask.FromResult(package.CallbackSubscriptionId ?? package.Manifest.PluginId);
            },
            localHandlers);
        LocalReactions.ConfigureDamageDecision(hooks);
        Assert.NotNull(lowered);

        using var server = PluginServer.Create(defaultPolicy: ProjectionPolicy());
        server.Events.Resolve<RemoteDamageDecisionEvent>();
        var kernel = await server.InstallAsync(lowered!);

        var requests = 0;
        RemoteLocalResultRequest request = (id, payload, ct) =>
        {
            Interlocked.Increment(ref requests);
            return localHandlers.DispatchResultAsync(id, payload, new HookContext(new InMemoryPluginMessageSink(), ct), ct);
        };

        // The router must classify this as ProjectingResult and call UseProjectingResult: the lowered Where runs
        // server-side and the result is requested from the plugin only for events that pass it.
        server.WireHook(kernel, new WireOptions { LocalResult = request });

        var miss = await server.Hooks.FireAsync<RemoteDamageDecisionEvent, RemoteDamageDecisionResult>(
            new RemoteDamageDecisionEvent("monster-1", 5));
        Assert.Null(miss);                       // filtered server-side: no result requested
        Assert.Equal(0, requests);

        var hit = await server.Hooks.FireAsync<RemoteDamageDecisionEvent, RemoteDamageDecisionResult>(
            new RemoteDamageDecisionEvent("monster-1", 12));
        Assert.True(hit!.Value.Success);
        Assert.Equal(24, hit.Value.Damage);      // context.ScaleDamageDecision(12) == 24
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task WireHook_throws_when_the_subscribed_event_adapter_is_not_registered()
    {
        var localHandlers = new RemoteLocalHandlerRegistry();
        var (package, _) = LowerCalmReaction(localHandlers, _ => { });

        using var server = PluginServer.Create(defaultPolicy: ProjectionPolicy());
        // Deliberately do NOT register the MonsterAggroEvent adapter: the router resolves by name and does not
        // auto-register, so wiring an unregistered event is rejected as "unsupported".
        var kernel = await server.InstallAsync(package);

        var ex = Assert.Throws<InvalidOperationException>(
            () => server.WireHook(kernel, new WireOptions { LocalPush = (_, _, _) => ValueTask.CompletedTask }));
        Assert.Contains("unsupported event", ex.Message, StringComparison.Ordinal);
    }

    private static (PluginPackage Package, string SubscriptionId) LowerCalmReaction(
        RemoteLocalHandlerRegistry localHandlers,
        Action<string> onCalmedMonster)
    {
        PluginPackage? lowered = null;
        string? subscriptionId = null;
        var hooks = new GamePluginHookRegistry(
            package =>
            {
                lowered = package;
                subscriptionId = package.CallbackSubscriptionId ?? package.Manifest.PluginId;
                return ValueTask.FromResult(subscriptionId);
            },
            localHandlers);
        LocalReactions.ConfigureCalmReaction(hooks, onCalmedMonster);
        Assert.NotNull(lowered);
        Assert.NotNull(subscriptionId);
        return (lowered!, subscriptionId!);
    }

    private static SandboxPolicy ProjectionPolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
