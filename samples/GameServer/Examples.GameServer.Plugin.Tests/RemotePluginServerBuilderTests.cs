using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
namespace DotBoxD.Kernels.Game.Plugin.Tests;
// Install ids are now DERIVED from the kernel type (GuardianKernel -> "guardian",
// MonsterKillerKernel -> "monster-killer"); the verbs are keyed purely by type. The literals asserted below
// are those derived values, not hand-typed ids.
public sealed partial class RemotePluginServerBuilderTests
{
    [Fact]
    public void FromConnection_build_performs_no_control_plane_io()
    {
        var control = new RecordingGamePluginControlService();
        using IGameWorldServer server = GamePluginServerBuilder
            .FromConnection(control)
            .Setup(s => s.Replace<IMonsterAggroService, GuardianKernel>())
            .Build();
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
        var killed = await server.Monsters.Get("monster-4").KillAsync();        // scoped handle, id captured
        var health = await server.Entities.Get("monster-4").GetHealthAsync();
        Assert.True(killed);
        Assert.Equal(42, health);
        Assert.Empty(control.Calls);
    }
    [Fact]
    public async Task Replace_and_Extend_install_packages_and_populate_lookup()
    {
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder
            .FromConnection(control, new FakeWorld())
            .Setup(s =>
            {
                s.Replace<IMonsterAggroService, GuardianKernel>();
                s.Replace<IAttackService, RetaliationKernel>();
                s.Monsters.Extend<MonsterKillerKernel>();
            })
            .Build();
        Assert.Empty(control.Calls);
        await server.StartAsync();
        Assert.Equal(["kernel:guardian", "kernel:retaliation", "extension:monster-killer"], control.Calls);
        Assert.Equal("monster-killer", server.ServerExtensions.PluginId<MonsterKillerKernel>());
    }
    [Fact]
    public async Task Set_builder_batches_live_settings_for_an_installed_kernel()
    {
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder
            .FromConnection(control, new FakeWorld())
            .Setup(s => s.Replace<IMonsterAggroService, GuardianKernel>())
            .Build();
        await server.StartAsync();
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
        using var server = GamePluginServerBuilder
            .FromConnection(control, new FakeWorld())
            .Setup(s => s.Monsters.Extend<MonsterKillerKernel>())
            .Build();
        await server.StartAsync();
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
    public async Task Generated_server_extensions_are_callable_after_two_arg_Extend()
    {
        // Regression: Extend<TService, TKernel> records the extension under BOTH the service type and the kernel
        // type, so the generated graft client — which looks the extension up by the KERNEL type — resolves it
        // instead of throwing "not registered". The explicit PluginId<TService>() lookup keeps working too.
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder
            .FromConnection(control, new FakeWorld())
            .Setup(s => s.Monsters.Extend<IMonsterControl, MonsterKillerKernel>())
            .Build();
        await server.StartAsync();

        var results = await server.Monsters.KillMonstersAsync(["monster-3", "monster-4"]);

        Assert.Equal("monster-killer", control.LastRpcPluginId);
        Assert.Equal("monster-killer", server.ServerExtensions.PluginId<MonsterKillerKernel>());
        Assert.Equal("monster-killer", server.ServerExtensions.PluginId<IMonsterControl>());
        Assert.Equal(["monster-3", "monster-4"], DecodeRequestedMonsterIds(control.LastRpcArguments));
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Generated_handle_extensions_send_receiver_id()
    {
        var control = new RecordingGamePluginControlService
        {
            RpcResponse = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(11))
        };
        using var server = GamePluginServerBuilder
            .FromConnection(control, new FakeWorld())
            .Setup(s => s.Monsters.Extend<BlinkKernel>())
            .Build();
        await server.StartAsync();
        var target = await server.Monsters.Get("monster-4").BlinkBehindAsync("player-1");
        Assert.Equal(11, target);
        Assert.Equal("blink", control.LastRpcPluginId);
        Assert.Equal(["monster-4", "player-1"], DecodeRequestedStrings(control.LastRpcArguments));
    }
    [Fact]
    public async Task Real_server_installs_handle_grafted_extension_package()
    {
        using var pluginServer = PluginServer.Create(
            configureHost: host => host.AddBindingsFrom<IGameWorldAccess>(new FakeWorld()));
        using var session = pluginServer.CreateSession();
        var package = KernelPackageRegistry.Resolve<BlinkKernel>();
        var policy = GrantRequiredCapabilities(pluginServer.GetRequiredCapabilities(package));
        var kernel = await session.InstallServerExtensionAsync(package, policy)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("blink", kernel.Manifest.PluginId);
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
        using var directServer = new GamePluginServer(directControl, new FakeWorld(), ConfigureSampleKernels);
        using var builderServer = GamePluginServerBuilder
            .FromConnection(builderControl, new FakeWorld())
            .Setup(ConfigureSampleKernels)
            .Build();
        await directServer.StartAsync();
        await builderServer.StartAsync();

        Assert.Equal(directControl.Calls, builderControl.Calls);
    }

    [Fact]
    public void InvokeAsync_stub_throws_when_call_site_is_not_intercepted()
    {
        var control = new RecordingGamePluginControlService();
        using var server = new GamePluginServer(control, new FakeWorld());

        var ex = Assert.Throws<InvalidOperationException>(
            () => server.InvokeAsync(async world => await world.Entities.Get("monster-1").GetHealthAsync()));

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

    private static void ConfigureSampleKernels(IGamePluginSetup setup)
    {
        setup.Replace<IMonsterAggroService, GuardianKernel>();
        setup.Replace<IAttackService, RetaliationKernel>();
        setup.Monsters.Extend<MonsterKillerKernel>();
    }

    private static string[] DecodeRequestedMonsterIds(byte[] arguments)
        => KernelRpcBinaryCodec.DecodeArguments(arguments)[0].Items.Select(item => item.TextValue).ToArray();

    private static string[] DecodeRequestedStrings(byte[] arguments)
        => KernelRpcBinaryCodec.DecodeArguments(arguments).Select(item => item.TextValue).ToArray();

    private static SandboxPolicy GrantRequiredCapabilities(IReadOnlyList<string> capabilities)
    {
        var builder = SandboxPolicyBuilder.Create()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000);
        foreach (var capability in capabilities)
        {
            if (string.Equals(capability, RuntimeCapabilityIds.Async, StringComparison.Ordinal))
            {
                builder.AllowRuntimeAsync();
                continue;
            }

            var effect = capability.Contains(".write.", StringComparison.Ordinal)
                ? SandboxEffect.HostStateWrite
                : SandboxEffect.HostStateRead;
            builder.Grant(capability, new { }, effect);
        }

        return builder.Build();
    }

}
