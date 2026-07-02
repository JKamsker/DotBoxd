using DotBoxD.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions;
namespace DotBoxD.Kernels.Game.Plugin.Tests;

public sealed partial class RemotePluginServerBuilderTests
{
    private sealed class FakeWorld : IGameWorldAccess
    {
        public FakeWorld()
        {
            Monsters = new FakeMonsterControl();
            Entities = new FakeEntityControl();
        }

        public IMonsterControl Monsters { get; }

        public IEntityControl Entities { get; }

        public IGoldLedger Gold { get; } = TestGoldLedger.Instance;
    }

    private sealed class FakeMonsterControl : IMonsterControl
    {
        public IMonster Get(string entityId) => new FakeMonster(entityId);

        [HostCapability("game.world.monster.read.kind", HostBindingEffect.HostStateRead)]
        public ValueTask<bool> IsMonsterAsync(string entityId)
            => ValueTask.FromResult(true);
    }

    private sealed class FakeEntityControl : IEntityControl
    {
        public IEntity Get(string entityId) => new FakeEntity(entityId);
    }

    private sealed class FakeMonster : IMonster
    {
        public FakeMonster(string id) => Id = id;

        public string Id { get; }

        public ValueTask<MonsterSnapshot> SnapshotAsync()
            => ValueTask.FromResult(new MonsterSnapshot(Id, Id, 42, 8, 5));

        public ValueTask<bool> KillAsync() => ValueTask.FromResult(true);

        public ValueTask<int> GetThreatAsync() => ValueTask.FromResult(7);

        public ValueTask TeleportToAsync(int position) => ValueTask.CompletedTask;

        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(42);

        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(8);

        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }

    private sealed class FakeEntity : IEntity
    {
        public FakeEntity(string id) => Id = id;

        public string Id { get; }

        public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(42);

        public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(8);

        public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(5);
    }
}
