using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

public sealed class RemotePluginServerBuilderSetupTests
{
    [Fact]
    public async Task Extend_with_explicit_service_type_registers_lookup_under_service_type()
    {
        var control = new RecordingGamePluginControlService();
        using var server = GamePluginServerBuilder
            .FromConnection(control, new FakeWorld())
            .Setup(s => s.Monsters.Extend<IMonsterControl, MonsterKillerKernel>())
            .Build();

        await server.StartAsync();

        Assert.Equal(["extension:monster-killer"], control.Calls);
        Assert.Equal("monster-killer", server.ServerExtensions.PluginId<IMonsterControl>());
    }

    private sealed class FakeWorld : IGameWorldAccess
    {
        public IMonsterControl Monsters { get; } = new FakeMonsterControl();

        public IEntityControl Entities { get; } = new FakeEntityControl();

        public IGoldLedger Gold { get; } = TestGoldLedger.Instance;
    }

    private sealed class FakeMonsterControl : IMonsterControl
    {
        public IMonster Get(string entityId) => new FakeMonster(entityId);

        public ValueTask<bool> IsMonsterAsync(string entityId) => ValueTask.FromResult(true);
    }

    private sealed class FakeEntityControl : IEntityControl
    {
        public IEntity Get(string entityId) => new FakeEntity(entityId);
    }

    private sealed class FakeMonster(string id) : IMonster
    {
        public string Id { get; } = id;

        public ValueTask<MonsterSnapshot> SnapshotAsync()
            => ValueTask.FromResult(new MonsterSnapshot(Id, Id, 42, 8, 5));

        public ValueTask<bool> KillAsync() => ValueTask.FromResult(true);

        public ValueTask<int> GetThreatAsync() => ValueTask.FromResult(7);

        public ValueTask TeleportToAsync(int position) => ValueTask.CompletedTask;

        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(42);

        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(8);

        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }

    private sealed class FakeEntity(string id) : IEntity
    {
        public string Id { get; } = id;

        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(42);

        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(8);

        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }
}
