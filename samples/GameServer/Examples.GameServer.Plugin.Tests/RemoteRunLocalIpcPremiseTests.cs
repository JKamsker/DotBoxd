using DotBoxD.Abstractions;
using DotBoxD.Kernels.Game.Plugin.Authoring;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Generated;
using DotBoxD.Services.Peer;
using DotBoxD.Transports.NamedPipes;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

/// <summary>
/// End-to-end proof that a remote <c>RunLocal</c> chain honours the 2-process premise across a REAL named-pipe
/// IPC boundary: <c>Where</c>+<c>Select</c> run server-side as verified IR, only the projected <c>MonsterId</c>
/// crosses the pipe (and only for events that pass the filter), and the native <c>RunLocal</c> delegate runs on
/// the plugin side. The chain is authored in the plugin project (<see cref="LocalReactions"/>) so the analyzer
/// lowers it; the install callback captures the lowered package and the per-event push is delivered through the
/// generated <see cref="IPluginEventCallback"/> over a live <see cref="RpcMessagePackIpc"/> pipe.
/// </summary>
/// <remarks>
/// This hard-fails if the premise breaks: if filtering leaked off the server, the per-event push count would
/// equal the number of events published (2) instead of the number that match the filter (1); if projection
/// leaked off the server, the raw event — not the scalar <c>MonsterId</c> — would have to cross the wire.
/// </remarks>
public sealed class RemoteRunLocalIpcPremiseTests
{
    // Plugin-side callback the server calls over the pipe: counts pushes and decodes the projected value back
    // into the native RunLocal delegate via the client-side registry.
    private sealed class CallbackSink(RemoteLocalHandlerRegistry registry) : IPluginEventCallback
    {
        private int _pushCount;
        private int _resultRequestCount;
        public int PushCount => Volatile.Read(ref _pushCount);
        public int ResultRequestCount => Volatile.Read(ref _resultRequestCount);

        public async ValueTask OnEventAsync(string subscriptionId, ReadOnlyMemory<byte> projectedValue, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _pushCount);
            await registry.DispatchAsync(
                subscriptionId,
                projectedValue,
                new HookContext(new InMemoryPluginMessageSink(), ct),
                ct);
        }

        public ValueTask<byte[]> OnResultAsync(
            string subscriptionId,
            ReadOnlyMemory<byte> contextValue,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _resultRequestCount);
            return registry.DispatchResultAsync(
                subscriptionId,
                contextValue,
                new HookContext(new InMemoryPluginMessageSink(), ct),
                ct);
        }
    }

    [Fact]
    public async Task RunLocal_chain_filters_and_projects_server_side_and_pushes_only_the_projection_over_ipc()
    {
        // --- Plugin authoring: ConfigureCalmReaction lowers Where+Select to a server-side projection kernel.
        // The install callback captures the lowered package; the native delegate registers client-side. ---
        var calmedOnPluginSide = new List<string>();
        var localHandlers = new RemoteLocalHandlerRegistry();
        PluginPackage? lowered = null;
        string? subscriptionId = null;
        var hooks = new RemoteHookRegistry(
            package =>
            {
                lowered = package;
                subscriptionId = package.Manifest.PluginId;
                return ValueTask.FromResult(package.Manifest.PluginId);
            },
            localHandlers);

        LocalReactions.ConfigureCalmReaction(hooks, monsterId =>
        {
            lock (calmedOnPluginSide)
            {
                calmedOnPluginSide.Add(monsterId);
            }
        });

        Assert.NotNull(lowered);
        Assert.NotNull(subscriptionId);
        // Premise: Where+Select lowered to a server-side projection kernel — marked local, needs no capability.
        var subscription = Assert.Single(lowered!.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);
        Assert.Empty(lowered.Manifest.RequiredCapabilities);

        // --- Live named-pipe IPC: two peers. The plugin PROVIDES the callback; the server GETS the proxy. ---
        var pipeName = "dotboxd-runlocal-e2e-" + Guid.NewGuid().ToString("N");
        var serverPeerReady = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var ipcHost = RpcMessagePackIpc.ListenNamedPipe(pipeName, peer => serverPeerReady.TrySetResult(peer));
        await ipcHost.StartAsync();

        var callbackSink = new CallbackSink(localHandlers);
        await using var clientSession = await RpcMessagePackIpc.ConnectAsync(
            new NamedPipeClientTransport(".", pipeName),
            peer => peer.ProvidePluginEventCallback(callbackSink));

        var serverPeer = await serverPeerReady.Task;
        var pushProxy = serverPeer.GetPluginEventCallback();

        // --- Server side: install the lowered package and wire the projecting push across the pipe. ---
        var serverMessages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(serverMessages, defaultPolicy: ProjectionPolicy());
        var kernel = await server.InstallAsync(lowered!);
        RemoteLocalPush push = (subId, payload, ct) => pushProxy.OnEventAsync(subId, payload, ct);
        server.Hooks.On<MonsterAggroEvent>().UseProjecting(kernel, subscriptionId!, push);

        // --- Drive events server-side: one matches the filter (Distance <= 4), one does not. PublishAsync
        // awaits the handler, which awaits the cross-pipe push, so delivery is observed without polling. ---
        await server.Hooks.PublishAsync(new MonsterAggroEvent("monster-7", "player-1", 3, 8, 1));   // matches
        await server.Hooks.PublishAsync(new MonsterAggroEvent("monster-9", "player-2", 10, 8, 1));  // filtered server-side

        // PREMISE 1: the filter ran SERVER-SIDE before any IPC — exactly one of two events crossed the pipe.
        Assert.Equal(1, callbackSink.PushCount);
        // PREMISE 2: the native RunLocal delegate ran on the PLUGIN side with only the projected MonsterId.
        Assert.Equal(["monster-7"], calmedOnPluginSide);
        // PREMISE 3: a projection terminal performs no host send — nothing else was produced server-side.
        Assert.Empty(serverMessages.Messages);
    }

    [Fact]
    public async Task WholeEvent_RunLocal_chain_filters_server_side_and_pushes_the_full_event_over_ipc()
    {
        // --- Plugin authoring: a no-Select whole-event RunLocal. The Where lowers server-side; the WHOLE
        // event record is pushed per matching event. Authored in the plugin project so the analyzer lowers it. ---
        var aggroOnPluginSide = new List<MonsterAggroEvent>();
        var localHandlers = new RemoteLocalHandlerRegistry();
        PluginPackage? lowered = null;
        string? subscriptionId = null;
        var hooks = new RemoteHookRegistry(
            package =>
            {
                lowered = package;
                subscriptionId = package.Manifest.PluginId;
                return ValueTask.FromResult(package.Manifest.PluginId);
            },
            localHandlers);

        LocalReactions.ConfigureWholeEventReaction(hooks, aggro =>
        {
            lock (aggroOnPluginSide)
            {
                aggroOnPluginSide.Add(aggro);
            }
        });

        Assert.NotNull(lowered);
        Assert.NotNull(subscriptionId);
        var subscription = Assert.Single(lowered!.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);
        Assert.Null(subscription.ProjectedType);   // no Select => whole-event push

        // --- Live named-pipe IPC: plugin PROVIDES the callback; server GETS the proxy. ---
        var pipeName = "dotboxd-runlocal-we-e2e-" + Guid.NewGuid().ToString("N");
        var serverPeerReady = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var ipcHost = RpcMessagePackIpc.ListenNamedPipe(pipeName, peer => serverPeerReady.TrySetResult(peer));
        await ipcHost.StartAsync();

        var callbackSink = new CallbackSink(localHandlers);
        await using var clientSession = await RpcMessagePackIpc.ConnectAsync(
            new NamedPipeClientTransport(".", pipeName),
            peer => peer.ProvidePluginEventCallback(callbackSink));

        var serverPeer = await serverPeerReady.Task;
        var pushProxy = serverPeer.GetPluginEventCallback();

        // --- Server side: install + wire the projecting push across the pipe. ---
        var serverMessages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(serverMessages, defaultPolicy: ProjectionPolicy());
        var kernel = await server.InstallAsync(lowered!);
        RemoteLocalPush push = (subId, payload, ct) => pushProxy.OnEventAsync(subId, payload, ct);
        server.Hooks.On<MonsterAggroEvent>().UseProjecting(kernel, subscriptionId!, push);

        var matching = new MonsterAggroEvent("monster-7", "player-1", 3, 8, 1);
        await server.Hooks.PublishAsync(matching);                                            // matches
        await server.Hooks.PublishAsync(new MonsterAggroEvent("monster-9", "player-2", 10, 8, 1)); // filtered server-side

        // PREMISE 1: filter ran SERVER-SIDE before any IPC — exactly one of two events crossed the pipe.
        Assert.Equal(1, callbackSink.PushCount);
        // PREMISE 2: the native delegate ran PLUGIN-side and received the FULL event record (all fields equal).
        Assert.Equal([matching], aggroOnPluginSide);
        // PREMISE 3: whole-event push performs no host send.
        Assert.Empty(serverMessages.Messages);
    }

    [Fact]
    public async Task RegisterLocal_result_chain_filters_server_side_and_returns_result_over_ipc()
    {
        var localHandlers = new RemoteLocalHandlerRegistry();
        PluginPackage? lowered = null;
        string? subscriptionId = null;
        var hooks = new RemoteHookRegistry(
            package =>
            {
                lowered = package;
                subscriptionId = package.Manifest.PluginId;
                return ValueTask.FromResult(package.Manifest.PluginId);
            },
            localHandlers);

        LocalReactions.ConfigureDamageDecision(hooks);

        Assert.NotNull(lowered);
        Assert.NotNull(subscriptionId);
        var subscription = Assert.Single(lowered!.Manifest.Subscriptions);
        Assert.True(subscription.ResultLocalTerminal);
        Assert.False(subscription.LocalTerminal);
        Assert.Equal(7, subscription.Priority);
        Assert.Equal(
            "global::DotBoxD.Kernels.Game.Server.Abstractions.Events.RemoteDamageDecisionResult",
            subscription.ResultType);

        var pipeName = "dotboxd-result-e2e-" + Guid.NewGuid().ToString("N");
        var serverPeerReady = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var ipcHost = RpcMessagePackIpc.ListenNamedPipe(pipeName, peer => serverPeerReady.TrySetResult(peer));
        await ipcHost.StartAsync();

        var callbackSink = new CallbackSink(localHandlers);
        await using var clientSession = await RpcMessagePackIpc.ConnectAsync(
            new NamedPipeClientTransport(".", pipeName),
            peer => peer.ProvidePluginEventCallback(callbackSink));

        var serverPeer = await serverPeerReady.Task;
        var pushProxy = serverPeer.GetPluginEventCallback();

        using var server = PluginServer.Create(defaultPolicy: ProjectionPolicy());
        var kernel = await server.InstallAsync(lowered!);
        RemoteLocalResultRequest request = (subId, payload, ct) => pushProxy.OnResultAsync(subId, payload, ct);
        server.Hooks.On<RemoteDamageDecisionEvent>()
            .UseProjectingResult(kernel, subscriptionId!, typeof(RemoteDamageDecisionResult), request, subscription.Priority);

        var miss = await server.Hooks.FireAsync<RemoteDamageDecisionEvent, RemoteDamageDecisionResult>(
            new RemoteDamageDecisionEvent("monster-1", 5));
        Assert.Null(miss);
        Assert.Equal(0, callbackSink.ResultRequestCount);

        var hit = await server.Hooks.FireAsync<RemoteDamageDecisionEvent, RemoteDamageDecisionResult>(
            new RemoteDamageDecisionEvent("monster-1", 12));
        Assert.True(hit!.Value.Success);
        Assert.Equal("remote", hit.Value.Reason);
        Assert.Equal(24, hit.Value.Damage);
        Assert.Equal(1, callbackSink.ResultRequestCount);
    }

    private static SandboxPolicy ProjectionPolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
