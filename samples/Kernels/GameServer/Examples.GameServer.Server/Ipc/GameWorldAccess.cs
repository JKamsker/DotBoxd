using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Simulation;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// The server's real implementation of <see cref="IGameWorldAccess"/> over the live <see cref="GameWorld"/>.
/// The plugin gets an RPC proxy of this same interface (its <c>GamePluginServer</c>); kernels get it injected.
/// Calls are synchronous against the in-process world, returned as completed <see cref="ValueTask"/>s — the
/// async shape only exists so the remote proxy and in-sandbox kernels share one contract.
/// <para>Because <see cref="IGameWorldAccess"/> is a pure domain contract (the install verbs live on the
/// generated plugin facade), this impl has <b>zero throwers</b>. <c>Get(id)</c> returns a scoped handle
/// (<see cref="GameMonster"/>/<see cref="GameEntity"/>) that captures the id; each handle method carries its
/// <see cref="HostCapabilityAttribute"/> — the single server-side source of capability metadata.</para>
/// </summary>
internal sealed class GameWorldAccess : IGameWorldAccess
{
    public GameWorldAccess(GameWorld world) : this(() => world)
    {
    }

    public GameWorldAccess(Func<GameWorld> world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Monsters = new GameMonsterControl(world);
        Entities = new GameEntityControl(world);
    }

    public IMonsterControl Monsters { get; }

    public IEntityControl Entities { get; }
}

internal sealed class GameMonsterControl : IMonsterControl
{
    private readonly Func<GameWorld> _world;

    public GameMonsterControl(Func<GameWorld> world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    public IMonster Get(string entityId) => new GameMonster(_world, entityId);

    [HostCapability("game.world.monster.read.kind")]
    public ValueTask<bool> IsMonsterAsync(string entityId)
        => ValueTask.FromResult(_world().IsMonster(entityId));
}

/// <summary>A monster handle scoped to <see cref="Id"/>; the id is captured so the methods omit it.</summary>
internal sealed class GameMonster : IMonster
{
    private readonly Func<GameWorld> _world;

    public GameMonster(Func<GameWorld> world, string id)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        Id = id;
    }

    public string Id { get; }

    [HostCapability("game.world.monster.read.snapshot")]
    public ValueTask<MonsterSnapshot> SnapshotAsync()
        => ValueTask.FromResult(_world().GetMonsterSnapshot(Id));

    [HostCapability("game.world.monster.write.kill")]   // effect (HostStateWrite) inferred from the impl
    public ValueTask<bool> KillAsync()
        => ValueTask.FromResult(_world().KillMonster(Id));

    // Deliberately a different capability subtree (combat.*, not monster.*) — the kind of exception a naming
    // convention could not infer, which is why the capability is stated explicitly here.
    [HostCapability("game.world.combat.threat")]
    public ValueTask<int> GetThreatAsync()
        => ValueTask.FromResult(_world().GetLevel(Id));

    [HostCapability("game.world.monster.write.position")]
    public ValueTask TeleportToAsync(int position)
    {
        _world().SetPosition(Id, position);
        return ValueTask.CompletedTask;
    }

    [HostCapability("game.world.entity.read.health")]
    public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(_world().GetHealth(Id));

    [HostCapability("game.world.entity.read.level")]
    public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(_world().GetLevel(Id));

    [HostCapability("game.world.entity.read.position")]
    public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(_world().GetPosition(Id));
}

internal sealed class GameEntityControl : IEntityControl
{
    private readonly Func<GameWorld> _world;

    public GameEntityControl(Func<GameWorld> world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    public IEntity Get(string entityId) => new GameEntity(_world, entityId);
}

/// <summary>An entity handle scoped to <see cref="Id"/>; entity reads are gated under entity.read.* so the
/// capability subtree matches the control the dev navigates (server.Entities.*).</summary>
internal sealed class GameEntity : IEntity
{
    private readonly Func<GameWorld> _world;

    public GameEntity(Func<GameWorld> world, string id)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        Id = id;
    }

    public string Id { get; }

    [HostCapability("game.world.entity.read.health")]
    public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(_world().GetHealth(Id));

    [HostCapability("game.world.entity.read.level")]
    public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(_world().GetLevel(Id));

    [HostCapability("game.world.entity.read.position")]
    public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(_world().GetPosition(Id));
}
