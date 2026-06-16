using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Simulation;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// The server's real implementation of <see cref="IGameWorldAccess"/> over the live <see cref="GameWorld"/>.
/// The plugin gets an RPC proxy of this same interface (its <c>GamePluginServer</c>); kernels get it injected.
/// Calls are synchronous against the in-process world, returned as completed <see cref="ValueTask"/>s — the
/// async shape only exists so the remote proxy and in-sandbox kernels share one contract.
/// <para>Because <see cref="IGameWorldAccess"/> is now a pure domain contract (the install verbs live on the
/// generated plugin facade), this impl has <b>zero throwers</b> — it implements exactly the methods it can
/// honor. Each domain method carries its <see cref="HostCapabilityAttribute"/>; this is the single
/// server-side source of capability metadata (the abstraction stays pure).</para>
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

    [HostCapability("game.world.monster.read.snapshot")]
    public ValueTask<MonsterSnapshot> GetAsync(string entityId)
        => ValueTask.FromResult(_world().GetMonsterSnapshot(entityId));

    [HostCapability("game.world.monster.write.kill")]   // effect (HostStateWrite) inferred from the impl
    public ValueTask<bool> KillAsync(string entityId)
        => ValueTask.FromResult(_world().KillMonster(entityId));

    [HostCapability("game.world.monster.read.kind")]
    public ValueTask<bool> IsMonsterAsync(string entityId)
        => ValueTask.FromResult(_world().IsMonster(entityId));

    // Deliberately a different capability subtree (combat.*, not monster.*) — the kind of exception that a
    // naming convention could not infer, which is exactly why the capability is stated explicitly here.
    [HostCapability("game.world.combat.threat")]
    public ValueTask<int> GetThreatAsync(string entityId)
        => ValueTask.FromResult(_world().GetLevel(entityId));
}

internal sealed class GameEntityControl : IEntityControl
{
    private readonly Func<GameWorld> _world;

    public GameEntityControl(Func<GameWorld> world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    // Entity reads are gated under entity.read.* so the capability subtree matches the control the dev
    // navigates (server.Entities.*), instead of being grouped under monster.read.*.
    [HostCapability("game.world.entity.read.health")]
    public ValueTask<int> GetHealthAsync(string entityId)
        => ValueTask.FromResult(_world().GetHealth(entityId));

    [HostCapability("game.world.entity.read.level")]
    public ValueTask<int> GetLevelAsync(string entityId)
        => ValueTask.FromResult(_world().GetLevel(entityId));

    [HostCapability("game.world.entity.read.position")]
    public ValueTask<int> GetPositionAsync(string entityId)
        => ValueTask.FromResult(_world().GetPosition(entityId));
}
