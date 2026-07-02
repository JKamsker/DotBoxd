using DotBoxD.Kernels.Game.Server.Simulation;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// The server's real implementation of <see cref="IGameWorldAccess"/> over the live <see cref="GameWorld"/>.
/// The plugin gets an RPC proxy of this same interface (its <c>GamePluginServer</c>); kernels get it injected.
/// Calls are synchronous against the in-process world, returned as completed <see cref="ValueTask"/>s — the
/// async shape only exists so the remote proxy and in-sandbox kernels share one contract.
/// <para>Because <see cref="IGameWorldAccess"/> is a pure domain contract (the install verbs live on the
/// generated plugin facade), this impl has <b>zero throwers</b>. <c>Get(id)</c> returns a scoped handle
/// (<see cref="GameMonster"/>/<see cref="GameEntity"/>) that captures the id; the SDK contract carries the
/// <see cref="HostBindingAttribute"/> metadata consumed by analyzer and runtime.</para>
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

    [HostBinding("game.world.monster.read.handle", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public IMonster Get(string entityId) => new GameMonster(_world, entityId);

    [HostBinding("game.world.monster.read.kind", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
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

    [HostBinding("game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
    public ValueTask<MonsterSnapshot> SnapshotAsync()
        => ValueTask.FromResult(_world().GetMonsterSnapshot(Id));

    [HostBinding("game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
    public ValueTask<bool> KillAsync()
        => ValueTask.FromResult(_world().KillMonster(Id));

    // Deliberately a different capability subtree (combat.*, not monster.*) — the kind of exception a naming
    // convention could not infer, which is why the capability is stated explicitly here.
    [HostBinding("game.world.combat.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public ValueTask<int> GetThreatAsync()
        => ValueTask.FromResult(_world().GetThreat(Id));

    [HostBinding("game.world.monster.write.position", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
    public ValueTask TeleportToAsync(int position)
    {
        _world().SetPosition(Id, position);
        return ValueTask.CompletedTask;
    }

    [HostBinding("game.world.entity.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(_world().GetHealth(Id));

    [HostBinding("game.world.entity.read.level", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(_world().GetLevel(Id));

    [HostBinding("game.world.entity.read.position", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(_world().GetPosition(Id));
}

internal sealed class GameEntityControl : IEntityControl
{
    private readonly Func<GameWorld> _world;

    public GameEntityControl(Func<GameWorld> world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    [HostBinding("game.world.entity.read.handle", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
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

    [HostBinding("game.world.entity.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public ValueTask<int> GetHealthAsync() => ValueTask.FromResult(_world().GetHealth(Id));

    [HostBinding("game.world.entity.read.level", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public ValueTask<int> GetLevelAsync() => ValueTask.FromResult(_world().GetLevel(Id));

    [HostBinding("game.world.entity.read.position", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public ValueTask<int> GetPositionAsync() => ValueTask.FromResult(_world().GetPosition(Id));
}
