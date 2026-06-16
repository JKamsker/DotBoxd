using DotBoxD.Kernels.Game.Plugin;
using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

// Install ids are now DERIVED from the kernel type (GuardianKernel -> "guardian",
// MonsterKillerKernel -> "monster-killer"); the verbs are keyed purely by type. The literals asserted below
// are those derived values, not hand-typed ids.
public sealed class RemotePluginServerBuilderTests
{
    [Fact]
    public void FromConnection_build_performs_no_control_plane_io()
    {
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder.FromConnection(control).Build();

        Assert.Empty(control.Calls);
        Assert.Throws<InvalidOperationException>(() => server.Monsters);
        Assert.Throws<InvalidOperationException>(() => server.Entities);
    }

    [Fact]
    public async Task FromConnection_wraps_existing_control_without_disposing_it()
    {
        var control = new RecordingGamePluginControlService();
        var server = GamePluginServerBuilder.FromConnection(control, new FakeWorld()).Build();

        await server.DisposeAsync();

        Assert.Equal(0, control.DisposeCount);
    }

    [Fact]
    public async Task Direct_domain_calls_forward_to_supplied_world_proxy()
    {
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder.FromConnection(control, new FakeWorld()).Build();

        var killed = await server.Monsters.KillAsync("monster-4");
        var health = await server.Entities.GetHealthAsync("monster-4");

        Assert.True(killed);
        Assert.Equal(42, health);
        Assert.Empty(control.Calls);
    }

    [Fact]
    public async Task Replace_and_Extend_install_packages_and_populate_lookup()
    {
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder.FromConnection(control, new FakeWorld()).Build();

        await server.Replace<IMonsterAggroService, GuardianKernel>();
        await server.Replace<IAttackService, RetaliationKernel>();
        await server.Monsters.Extend<MonsterKillerKernel>();

        Assert.Equal(["kernel:guardian", "kernel:retaliation", "extension:monster-killer"], control.Calls);
        Assert.Equal("monster-killer", server.Monsters.ServerExtensions.PluginId<MonsterKillerKernel>());
    }

    [Fact]
    public async Task Set_builder_batches_live_settings_for_an_installed_kernel()
    {
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder.FromConnection(control, new FakeWorld()).Build();

        await server.Replace<IMonsterAggroService, GuardianKernel>();
        await server.Get<GuardianKernel>()
            .Set(k => k.CalmStrength, 35)
            .Set(k => k.AggroRange, 6)
            .ApplyAsync(atomic: true);

        Assert.Equal(["kernel:guardian", "settings:guardian"], control.Calls);
    }

    [Fact]
    public async Task Generated_server_extensions_are_callable_after_Extend()
    {
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder.FromConnection(control, new FakeWorld()).Build();

        await server.Monsters.Extend<MonsterKillerKernel>();
        var results = await server.Monsters.KillMonstersAsync(["monster-3", "monster-4"]);

        Assert.Equal("monster-killer", control.LastRpcPluginId);
        Assert.Equal(["monster-3", "monster-4"], DecodeRequestedMonsterIds(control.LastRpcArguments));
        Assert.Collection(
            results,
            result =>
            {
                Assert.Equal("monster-3", result.MonsterId);
                Assert.True(result.WasMonster);
                Assert.True(result.Killed);
            },
            result =>
            {
                Assert.Equal("monster-4", result.MonsterId);
                Assert.True(result.WasMonster);
                Assert.False(result.Killed);
            });
    }

    [Fact]
    public async Task FromPipeName_defers_pipe_validation_until_StartAsync()
    {
        await using var server = GamePluginServerBuilder.FromPipeName("unsafe").Build();

        await Assert.ThrowsAsync<ArgumentException>(async () => await server.StartAsync().AsTask());
    }

    [Fact]
    public async Task RunAsync_holds_until_shutdown()
    {
        var control = new RecordingGamePluginControlService();
        await using var server = GamePluginServerBuilder.FromConnection(control, new FakeWorld()).Build();

        var runTask = server.RunAsync().AsTask();
        await control.HoldStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["hold"], control.Calls);
        Assert.False(runTask.IsCompleted);

        control.SignalShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Constructor_and_builder_paths_produce_equivalent_install_sequence()
    {
        var directControl = new RecordingGamePluginControlService();
        var builderControl = new RecordingGamePluginControlService();
        using var directServer = new GamePluginServer(directControl, new FakeWorld());
        using var builderServer = GamePluginServerBuilder.FromConnection(builderControl, new FakeWorld()).Build();

        await InstallSampleKernels(directServer);
        await InstallSampleKernels(builderServer);

        Assert.Equal(directControl.Calls, builderControl.Calls);
    }

    [Fact]
    public void InvokeAsync_stub_throws_when_call_site_is_not_intercepted()
    {
        var control = new RecordingGamePluginControlService();
        using var server = new GamePluginServer(control, new FakeWorld());

        var ex = Assert.Throws<InvalidOperationException>(
            () => server.InvokeAsync(async world => await world.Entities.GetHealthAsync("monster-1")));

        Assert.Contains("must be intercepted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_anonymous_kernel_installs_share_one_registration()
    {
        var control = new RecordingGamePluginControlService();
        using var server = new GamePluginServer(control, new FakeWorld());
        var factoryCalls = 0;

        var installs = Enumerable.Range(0, 16)
            .Select(_ => server.Services.EnsureAnonymousKernelAsync("monster-killer", () =>
            {
                Interlocked.Increment(ref factoryCalls);
                return KernelPackageRegistry.Resolve<MonsterKillerKernel>();
            }))
            .ToArray();

        var installedIds = await Task.WhenAll(installs);

        Assert.All(installedIds, id => Assert.Equal("monster-killer", id));
        Assert.Equal(1, factoryCalls);
        Assert.Equal(["extension:monster-killer"], control.Calls);
    }

    private static async Task InstallSampleKernels(GamePluginServer server)
    {
        await server.Replace<IMonsterAggroService, GuardianKernel>();
        await server.Replace<IAttackService, RetaliationKernel>();
        await server.Monsters.Extend<MonsterKillerKernel>();
    }

    private static string[] DecodeRequestedMonsterIds(byte[] arguments)
        => KernelRpcBinaryCodec.DecodeArguments(arguments)[0].Items.Select(item => item.TextValue).ToArray();

    private sealed class FakeWorld : IGameWorldAccess
    {
        public FakeWorld()
        {
            Monsters = new FakeMonsterControl();
            Entities = new FakeEntityControl();
        }

        public IMonsterControl Monsters { get; }

        public IEntityControl Entities { get; }
    }

    private sealed class FakeMonsterControl : IMonsterControl
    {
        public ValueTask<MonsterSnapshot> GetAsync(string entityId)
            => ValueTask.FromResult(new MonsterSnapshot(entityId, entityId, 42, 8, 5));

        public ValueTask<bool> KillAsync(string entityId)
            => ValueTask.FromResult(true);

        public ValueTask<bool> IsMonsterAsync(string entityId)
            => ValueTask.FromResult(true);

        public ValueTask<int> GetThreatAsync(string entityId)
            => ValueTask.FromResult(7);
    }

    private sealed class FakeEntityControl : IEntityControl
    {
        public ValueTask<int> GetHealthAsync(string entityId)
            => ValueTask.FromResult(42);

        public ValueTask<int> GetLevelAsync(string entityId)
            => ValueTask.FromResult(8);

        public ValueTask<int> GetPositionAsync(string entityId)
            => ValueTask.FromResult(5);
    }
}
