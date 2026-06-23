using DotBoxD.Abstractions;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

public sealed class BlinkServerExtensionRegressionTests
{
    [Theory]
    [InlineData(ExecutionMode.Auto)]
    [InlineData(ExecutionMode.Compiled)]
    [InlineData(ExecutionMode.Interpreted)]
    public async Task Blink_server_extension_installs_without_effect_drift_and_invokes(ExecutionMode mode)
    {
        using var pluginServer = PluginServer.Create(
            configureHost: host => host.AddBindingsFrom<IGameWorldAccess>(new StubWorld()),
            executionMode: mode);
        using var session = pluginServer.CreateSession();
        var package = KernelPackageRegistry.Resolve<BlinkKernel>();
        Assert.Equal(["Concurrency", "Cpu", "HostStateRead", "HostStateWrite"],
            package.Manifest.Effects.OrderBy(static e => e, StringComparer.Ordinal).ToArray());
        var policy = GrantRequiredCapabilities(pluginServer.GetRequiredCapabilities(package));
        var kernel = await session.InstallServerExtensionAsync(package, policy)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("blink", kernel.Manifest.PluginId);
        var result = await kernel.InvokeServerExtensionAsync(
                [SandboxValue.FromString("monster-4"), SandboxValue.FromString("player-1")])
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, Assert.IsType<I32Value>(result).Value);
    }

    private static SandboxPolicy GrantRequiredCapabilities(IReadOnlyList<string> capabilities)
    {
        var builder = SandboxPolicyBuilder.Create().WithFuel(100_000).WithMaxHostCalls(1_000);
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

    private sealed class StubWorld : IGameWorldAccess
    {
        public IMonsterControl Monsters { get; } = new StubMonsterControl();
        public IEntityControl Entities { get; } = new StubEntityControl();
    }

    private sealed class StubMonsterControl : IMonsterControl
    {
        public IMonster Get(string entityId) => new StubMonster(entityId);

        [HostCapability("game.world.monster.read.kind")]
        public ValueTask<bool> IsMonsterAsync(string entityId) => ValueTask.FromResult(true);
    }

    private sealed class StubEntityControl : IEntityControl
    {
        public IEntity Get(string entityId) => new StubEntity(entityId);
    }

    private sealed class StubMonster(string id) : IMonster
    {
        public string Id { get; } = id;
        public ValueTask<MonsterSnapshot> SnapshotAsync() => ValueTask.FromResult(new MonsterSnapshot(Id, Id, 80, 8, 5));
        public ValueTask<bool> KillAsync() => ValueTask.FromResult(true);
        public ValueTask<int> GetThreatAsync() => ValueTask.FromResult(8);
        public ValueTask TeleportToAsync(int position) => ValueTask.CompletedTask;
        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(80);
        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(8);
        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }

    private sealed class StubEntity(string id) : IEntity
    {
        public string Id { get; } = id;
        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(30);
        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(1);
        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }
}
