using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0032: class-shaped live kernel state caches its
/// discovered live-property shape per state type so the hot synchronization paths
/// (draft creation, extract, copy, pull-from-store, and per-input push) no longer
/// rediscover <c>PropertyInfo</c> metadata on every modify/input/refresh. Caching
/// the immutable property shape must not change any observable behavior, so these
/// tests drive the same paths repeatedly and assert the synchronized state stays
/// correct across draft modifications, host-side setting refreshes, and hook input
/// construction.
/// </summary>
public sealed class Fix_PAL_0032_Tests
{
    [Fact]
    public async Task Repeated_class_modify_keeps_state_and_store_in_sync()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        // Drive CreateDraft/ExtractSettings/CopyLiveProperties/PullFromStore
        // repeatedly; each iteration re-uses the cached property shape.
        for (var i = 1; i <= 5; i++)
        {
            var expected = 100 + i;
            var expectedType = i % 2 == 0 ? "fire" : "ice";
            await kernel.ModifyAsync(state =>
            {
                state.MinDamage = expected;
                state.DamageType = expectedType;
            });

            Assert.Equal(expected, kernel.Value.MinDamage);
            Assert.Equal(expectedType, kernel.Value.DamageType);
            Assert.Equal(expected, kernel.Kernel.Value.Get<int>("MinDamage"));
            Assert.Equal(expectedType, kernel.Kernel.Value.Get<string>("DamageType"));
        }
    }

    [Fact]
    public async Task Host_side_setting_refresh_pulls_into_cached_class_state()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        // External host modification refreshes typed values from the store via
        // PullFromStore over the cached property shape.
        await installed.ModifySettingsAsync(new Dictionary<string, object?>
        {
            ["DamageType"] = "ice",
            ["MinDamage"] = 250
        });

        Assert.Equal("ice", kernel.Value.DamageType);
        Assert.Equal(250, kernel.Value.MinDamage);
    }

    [Fact]
    public async Task Per_input_sync_pushes_cached_class_state_before_dispatch()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        // Mutating the live state object directly relies on SynchronizeForInput
        // (PushToStore over the cached shape) to flush before each dispatch.
        kernel.Value.DamageType = "ice";
        kernel.Value.MinDamage = 200;

        await server.Hooks.PublishAsync(new DamageEvent("fire", 300, "player-1"));
        await server.Hooks.PublishAsync(new DamageEvent("ice", 150, "player-2"));
        await server.Hooks.PublishAsync(new DamageEvent("ice", 250, "player-3"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-3", message.TargetId);
        Assert.Equal("ice", kernel.Kernel.Value.Get<string>("DamageType"));
        Assert.Equal(200, kernel.Kernel.Value.Get<int>("MinDamage"));
    }
}
