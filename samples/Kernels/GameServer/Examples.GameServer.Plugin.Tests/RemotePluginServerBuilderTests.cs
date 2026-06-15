using DotBoxD.Kernels.Game.Plugin;
using DotBoxD.Kernels.Game.Plugin.Client;
using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

public sealed class RemotePluginServerBuilderTests
{
    [Fact]
    public void Build_performs_no_io()
    {
        var control = new RecordingGamePluginControlService();
        var server = BuildServer(control);

        Assert.Empty(control.Calls);
        Assert.Throws<InvalidOperationException>(() => server.Kernels);
        Assert.Throws<InvalidOperationException>(() => server.KernelRpc);
        Assert.Throws<InvalidOperationException>(() => server.World);
    }

    [Fact]
    public async Task StartAsync_registers_all_kernels_before_returning()
    {
        var control = new RecordingGamePluginControlService();
        await using var server = BuildServer(control);

        await server.StartAsync();

        Assert.Equal(["kernel:guardian", "kernel:retaliation", "rpc:monster-killer"], control.Calls);
    }

    [Fact]
    public async Task StartAsync_populates_rpc_service_lookup()
    {
        var control = new RecordingGamePluginControlService();
        await using var server = BuildServer(control);

        await server.StartAsync();

        Assert.Equal("monster-killer", server.KernelRpc.PluginId<IMonsterKillerService>());
    }

    [Fact]
    public async Task Generated_rpc_extensions_are_callable_immediately_after_StartAsync()
    {
        var control = new RecordingGamePluginControlService();
        await using var server = BuildServer(control);

        await server.StartAsync();
        var results = await server.World.Monsters.KillMonstersAsync(["monster-3", "monster-4"]);

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
    public async Task FromConnection_wraps_existing_control_without_disposing_it()
    {
        var control = new RecordingGamePluginControlService();
        var server = RemotePluginServerBuilder.FromConnection(control).Build();

        await server.StartAsync();
        await server.DisposeAsync();

        Assert.Equal(0, control.DisposeCount);
    }

    [Fact]
    public async Task Factory_connection_is_deferred_until_StartAsync_and_owned_connection_is_disposed()
    {
        var control = new RecordingGamePluginControlService();
        var ownedConnection = new RecordingAsyncDisposable();
        var connectCount = 0;
        await using var server = RemotePluginServerBuilder
            .FromConnectionFactory(cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                connectCount++;
                return ValueTask.FromResult(new RemotePluginConnection(control, ownedConnection));
            })
            .Build();

        Assert.Equal(0, connectCount);

        await server.StartAsync();
        Assert.Equal(1, connectCount);

        await server.DisposeAsync();
        Assert.Equal(1, ownedConnection.DisposeCount);
    }

    [Fact]
    public async Task FromPipeName_defers_pipe_validation_until_StartAsync()
    {
        await using var server = RemotePluginServerBuilder.FromPipeName("unsafe").Build();

        await Assert.ThrowsAsync<ArgumentException>(async () => await server.StartAsync().AsTask());
    }

    [Fact]
    public async Task RunAsync_starts_then_holds_until_shutdown()
    {
        var control = new RecordingGamePluginControlService();
        await using var server = BuildServer(control);

        var runTask = server.RunAsync().AsTask();
        await control.HoldStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            ["kernel:guardian", "kernel:retaliation", "rpc:monster-killer", "hold"],
            control.Calls);
        Assert.False(runTask.IsCompleted);

        control.SignalShutdown();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Imperative_and_builder_paths_produce_equivalent_install_sequence()
    {
        var imperativeControl = new RecordingGamePluginControlService();
        var builderControl = new RecordingGamePluginControlService();
        var imperativeServer = new RemotePluginServer(imperativeControl);
        await using var builderServer = BuildServer(builderControl);

        _ = await imperativeServer.Kernels.Register<IMonsterAggroService, GuardianKernel>();
        _ = await imperativeServer.Kernels.Register<IAttackService, RetaliationKernel>();
        _ = await imperativeServer.KernelRpc.Register<IMonsterKillerService, MonsterKillerKernel>();
        await builderServer.StartAsync();

        Assert.Equal(imperativeControl.Calls, builderControl.Calls);
    }

    [Fact]
    public void KernelRpc_accumulator_accepts_kernel_not_implementing_service()
    {
        var control = new RecordingGamePluginControlService();
        var builder = RemotePluginServerBuilder
            .FromConnection(control)
            .SetupKernelRpc(rpc => rpc.Register<IMonsterKillerService, MonsterKillerKernel>());

        Assert.NotNull(builder.Build());
    }

    private static RemotePluginServer BuildServer(RecordingGamePluginControlService control)
        => RemotePluginServerBuilder
            .FromConnection(control)
            .SetupKernels(kernels => kernels
                .Register<IMonsterAggroService, GuardianKernel>()
                .Register<IAttackService, RetaliationKernel>())
            .SetupKernelRpc(rpc => rpc
                .Register<IMonsterKillerService, MonsterKillerKernel>())
            .Build();

    private static string[] DecodeRequestedMonsterIds(byte[] arguments)
        => KernelRpcBinaryCodec.DecodeArguments(arguments)[0].Items.Select(item => item.TextValue).ToArray();

    private sealed class RecordingAsyncDisposable : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
