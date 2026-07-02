using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Plugin.Tests.Regression;

public sealed class MonsterKillerServerExtensionRegressionTests
{
    [Fact]
    public async Task Rpc_invocation_returns_list_of_record_structs()
    {
        using var pluginServer = PluginServer.Create(
            configureHost: host => host.AddBindingsFrom<IGameWorldAccess>(new StubWorld()),
            executionMode: ExecutionMode.Compiled);
        using var session = pluginServer.CreateSession();
        var package = KernelPackageRegistry.Resolve<MonsterKillerKernel>();
        var policy = GrantRequiredCapabilities(pluginServer.GetRequiredCapabilities(package));
        var kernel = await session.InstallServerExtensionAsync(package, policy)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var request = KernelRpcBinaryCodec.EncodeArguments(
        [
            KernelRpcValue.List(
            [
                KernelRpcValue.String("monster-3"),
                KernelRpcValue.String("player-1")
            ])
        ]);

        var response = await kernel.InvokeServerExtensionRpcAsync(request)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));

        var results = KernelRpcBinaryCodec.DecodeValue(response);
        results.RequireKind(KernelRpcValueKind.List);
        Assert.Equal(2, results.ItemCount);
        AssertKillResult(results.GetItem(0), "monster-3", wasMonster: true, level: 8, position: 5, healthBefore: 42, killed: true);
        AssertKillResult(results.GetItem(1), "player-1", wasMonster: false, level: 1, position: 0, healthBefore: 30, killed: false);
    }

    private static void AssertKillResult(
        KernelRpcValue result,
        string monsterId,
        bool wasMonster,
        int level,
        int position,
        int healthBefore,
        bool killed)
    {
        result.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(monsterId, result.GetItem(0).TextValue);
        Assert.Equal(wasMonster, result.GetItem(1).BoolValue);
        Assert.Equal(level, result.GetItem(2).Int32Value);
        Assert.Equal(position, result.GetItem(3).Int32Value);
        Assert.Equal(healthBefore, result.GetItem(4).Int32Value);
        Assert.Equal(killed, result.GetItem(5).BoolValue);
    }

    private static SandboxPolicy GrantRequiredCapabilities(IReadOnlyList<string> capabilities)
    {
        var builder = SandboxPolicyBuilder.Create()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10));
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

        public IGoldLedger Gold { get; } = TestGoldLedger.Instance;
    }

    private sealed class StubMonsterControl : IMonsterControl
    {
        public IMonster Get(string entityId) => new StubMonster(entityId, IsMonster(entityId));

        public ValueTask<bool> IsMonsterAsync(string entityId)
            => ValueTask.FromResult(IsMonster(entityId));

        private static bool IsMonster(string entityId)
            => entityId.StartsWith("monster-", StringComparison.Ordinal);
    }

    private sealed class StubEntityControl : IEntityControl
    {
        public IEntity Get(string entityId) => new StubEntity(entityId);
    }

    private sealed class StubMonster(string id, bool isMonster) : IMonster
    {
        public string Id { get; } = id;
        public ValueTask<MonsterSnapshot> SnapshotAsync()
            => ValueTask.FromResult(isMonster
                ? new MonsterSnapshot(Id, Id, 42, 8, 5)
                : new MonsterSnapshot(Id, string.Empty, 0, 0, 0));

        public ValueTask<bool> KillAsync()
            => isMonster
                ? ValueTask.FromResult(true)
                : throw new InvalidOperationException($"Unexpected monster kill for '{Id}'.");

        public ValueTask<int> GetThreatAsync() => ValueTask.FromResult(isMonster ? 7 : 0);
        public ValueTask TeleportToAsync(int position) => ValueTask.CompletedTask;
        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(isMonster ? 42 : 30);
        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(isMonster ? 8 : 1);
        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(isMonster ? 5 : 0);
    }

    private sealed class StubEntity(string id) : IEntity
    {
        public string Id { get; } = id;
        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(42);
        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(8);
        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }
}
