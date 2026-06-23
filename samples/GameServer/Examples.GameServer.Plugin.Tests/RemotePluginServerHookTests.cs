using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

public sealed class RemotePluginServerHookTests
{
    [Fact]
    public async Task Setup_hooks_are_replayed_when_the_generated_server_starts()
    {
        var control = new RecordingGamePluginControlService();
        await using IGameWorldServer server = GamePluginServerBuilder
            .FromConnection(control)
            .Setup(s => s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>())
            .Build();

        Assert.Empty(control.Calls);

        await server.StartAsync();

        Assert.Equal(["kernel:guardian"], control.Calls);
    }

    [Fact]
    public async Task Setup_subscriptions_are_replayed_when_the_generated_server_starts()
    {
        var control = new RecordingGamePluginControlService();
        await using IGameWorldServer server = GamePluginServerBuilder
            .FromConnection(control)
            .Setup(s => s.Subscriptions.On<AttackEvent>().Use<RetaliationKernel>())
            .Build();

        Assert.Empty(control.Calls);

        await server.StartAsync();

        Assert.Equal(["subscription:retaliation"], control.Calls);
    }

    [Fact]
    public async Task Generated_server_interface_exposes_hooks_after_StartAsync()
    {
        var control = new RecordingGamePluginControlService();
        await using IGameWorldServer server = GamePluginServerBuilder
            .FromConnection(control)
            .Build();

        await server.StartAsync();

        server.Hooks.On<AttackEvent>().Use<RetaliationKernel>();

        Assert.Equal(["kernel:retaliation"], control.Calls);
    }

    [Fact]
    public async Task Generated_server_interface_exposes_subscriptions_after_StartAsync()
    {
        var control = new RecordingGamePluginControlService();
        await using IGameWorldServer server = GamePluginServerBuilder
            .FromConnection(control)
            .Build();

        await server.StartAsync();

        server.Subscriptions.On<AttackEvent>().Use<RetaliationKernel>();

        Assert.Equal(["subscription:retaliation"], control.Calls);
    }

    [Fact]
    public async Task Example_runtime_hooks_install_subscriptions_and_remote_chains_after_StartAsync()
    {
        var control = new RecordingGamePluginControlService();
        await using IGameWorldServer server = GamePluginServerBuilder
            .FromConnection(control)
            .Build();

        await server.StartAsync();

        Program.ConfigureRuntimeHooks(server);

        // One inline hook chain (MonsterAggroEvent calm) using the generated plugin-owned GamePluginContext,
        // then two inline subscription chains on AttackEvent:
        // the original taunt and the indexed taunt that ships index metadata (issue #47).
        Assert.Collection(
            control.Calls,
            call => Assert.StartsWith("kernel:chain-", call, StringComparison.Ordinal),
            call => Assert.StartsWith("subscription:chain-", call, StringComparison.Ordinal),
            call => Assert.StartsWith("subscription:chain-", call, StringComparison.Ordinal));
    }
}
