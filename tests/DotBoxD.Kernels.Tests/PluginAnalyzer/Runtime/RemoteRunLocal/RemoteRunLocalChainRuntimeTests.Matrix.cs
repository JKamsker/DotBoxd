using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// Subscription-surface and payload-isolation cells of the RunLocal premise matrix (partial of
/// <see cref="RemoteRunLocalChainRuntimeTests"/>; shares its source constants + Compile/LowerToPackage/
/// ChainPolicy helpers). Split out to keep each test file within the repo's per-file length limit.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    [Fact]
    public async Task Subscription_surface_filters_and_projects_server_side_pushing_only_matching_events()
    {
        var package = LowerToPackage(RemoteRunLocalSource);
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package);

        var pushed = new List<byte[]>();
        TaskCompletionSource? gate = null;
        RemoteLocalPush push = (_, payload, _) =>
        {
            lock (pushed)
            {
                // Snapshot eagerly — the payload aliases a pooled buffer returned once the push completes.
                pushed.Add(payload.ToArray());
            }

            Volatile.Read(ref gate)?.TrySetResult();
            return ValueTask.CompletedTask;
        };
        server.Subscriptions.On<ChainAggroEvent>().UseProjecting(kernel, "sub-sp", push);

        // Subscription publish is fire-and-forget: deterministically await the matching event's push.
        var matched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref gate, matched);
        server.Subscriptions.Publish(new ChainAggroEvent("monster-7", 3));   // 3 <= 4 → matches
        await matched.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(pushed);

        // PREMISE: the non-matching event must be dropped by the server-side filter BEFORE any push — assert no
        // second push arrives in a bounded window (hard-fails if filtering leaked: it would push within ms).
        var shouldNotFire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref gate, shouldNotFire);
        server.Subscriptions.Publish(new ChainAggroEvent("monster-9", 10));  // 10 > 4 → filtered server-side
        await Assert.ThrowsAsync<TimeoutException>(() => shouldNotFire.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Single(pushed);            // still exactly one — the filter ran server-side
        Assert.Empty(messages.Messages);  // projection performs no host send

        // Only the projected scalar (String) crossed — NOT the whole event record.
        Assert.Equal(KernelRpcValueKind.String, KernelRpcBinaryCodec.DecodeValue(pushed[0]).Kind);
        var clientRegistry = new RemoteLocalHandlerRegistry();
        string? received = null;
        clientRegistry.Register<string>("sub-sp", (id, _) =>
        {
            received = id;
            return ValueTask.CompletedTask;
        });
        await clientRegistry.DispatchAsync("sub-sp", pushed[0], new HookContext(messages, CancellationToken.None));
        Assert.Equal("monster-7", received);
    }

    [Fact]
    public async Task Subscription_surface_filters_then_pushes_the_whole_event_record_for_matching_events()
    {
        var package = LowerToPackage(RemoteWholeEventSource);
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package);

        var pushed = new List<byte[]>();
        TaskCompletionSource? gate = null;
        RemoteLocalPush push = (_, payload, _) =>
        {
            lock (pushed)
            {
                pushed.Add(payload.ToArray());
            }

            Volatile.Read(ref gate)?.TrySetResult();
            return ValueTask.CompletedTask;
        };
        server.Subscriptions.On<ChainAggroEvent>().UseProjecting(kernel, "sub-swe", push);

        var matched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref gate, matched);
        server.Subscriptions.Publish(new ChainAggroEvent("monster-7", 3));   // matches
        await matched.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(pushed);

        var shouldNotFire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref gate, shouldNotFire);
        server.Subscriptions.Publish(new ChainAggroEvent("monster-9", 10));  // filtered server-side
        await Assert.ThrowsAsync<TimeoutException>(() => shouldNotFire.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Single(pushed);
        Assert.Empty(messages.Messages);

        // The whole event RECORD crossed (not a scalar) and round-trips to the original on the client.
        Assert.Equal(KernelRpcValueKind.Record, KernelRpcBinaryCodec.DecodeValue(pushed[0]).Kind);
        var clientRegistry = new RemoteLocalHandlerRegistry();
        ChainAggroEvent? received = null;
        clientRegistry.Register<ChainAggroEvent>("sub-swe", (evt, _) =>
        {
            received = evt;
            return ValueTask.CompletedTask;
        });
        await clientRegistry.DispatchAsync("sub-swe", pushed[0], new HookContext(messages, CancellationToken.None));
        Assert.Equal(new ChainAggroEvent("monster-7", 3), received);
    }

    [Fact]
    public async Task Projection_push_carries_only_the_scalar_not_the_whole_event_record()
    {
        var package = LowerToPackage(RemoteRunLocalSource);
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package);

        byte[]? payload = null;
        server.Hooks.On<ChainAggroEvent>().UseProjecting(kernel, "sub-scalar", (_, p, _) =>
        {
            // Snapshot eagerly — the payload aliases a pooled buffer returned once the push completes.
            payload = p.ToArray();
            return ValueTask.CompletedTask;
        });
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-7", 3));

        Assert.NotNull(payload);
        // Only the projected scalar (String) crosses — hard-fails if the whole event record leaked (Record).
        Assert.Equal(KernelRpcValueKind.String, KernelRpcBinaryCodec.DecodeValue(payload!).Kind);
    }
}
