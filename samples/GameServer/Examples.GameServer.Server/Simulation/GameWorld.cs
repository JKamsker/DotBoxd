using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins.Indexing;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Game.Server.Simulation;

/// <summary>
/// A small deterministic simulation of players and monsters on a 1D integer line. There is no RNG:
/// given the same starting entities and the same plugin commands, every run produces identical
/// output. The world publishes <see cref="MonsterAggroEvent"/> and <see cref="AttackEvent"/> through
/// hooks and fire-and-forget subscriptions, then applies any plugin commands the command sink recorded.
/// </summary>
internal sealed class GameWorld
{
    public const int AggroRange = 5;

    // A monster that has accumulated at least this much calm toward a player will hunt someone else.
    public const int CalmSuppressionThreshold = 30;

    private readonly List<GameEntity> _entities;
    private readonly HookRegistry _hooks;
    private readonly SubscriptionRegistry _subscriptions;
    private int _tick;

    public GameWorld(IEnumerable<GameEntity> entities, HookRegistry hooks, SubscriptionRegistry subscriptions)
    {
        _entities = [.. entities];
        _hooks = hooks;
        _subscriptions = subscriptions;
    }

    public int Tick => _tick;

    /// <summary>
    /// The host's dispatch index (issue #49). Subscriptions whose lowered <c>.Where(...)</c> shipped index
    /// metadata over <see cref="EventIndexKeyAttribute"/> fields are routed here instead of the broad
    /// subscription pipeline: each published event is cheaply prefiltered before the verified IR runs, so
    /// the sandbox is entered only for events that pass the index. Created internally so the existing
    /// reflection-pinned <see cref="GameWorld"/> constructor and <c>CreateDefault</c> shapes stay stable.
    /// </summary>
    public EventIndexRegistry IndexRegistry { get; } = new();

    public static GameWorld CreateDefault(HookRegistry hooks, SubscriptionRegistry subscriptions)
        => new(
            [
                new GameEntity("player-1", EntityKind.Player, level: 1, hp: 30, position: 0),
                new GameEntity("player-2", EntityKind.Player, level: 3, hp: 30, position: 4),
                new GameEntity("monster-1", EntityKind.Monster, level: 8, hp: 80, position: 7),
                new GameEntity("monster-2", EntityKind.Monster, level: 8, hp: 80, position: 12),
                new GameEntity("monster-3", EntityKind.Monster, level: 6, hp: 55, position: 80),
                new GameEntity("monster-4", EntityKind.Monster, level: 6, hp: 45, position: 90)
            ],
            hooks,
            subscriptions);

    public WorldSnapshot Snapshot()
    {
        var snapshots = new EntitySnapshot[_entities.Count];
        for (var i = 0; i < _entities.Count; i++)
        {
            snapshots[i] = _entities[i].ToSnapshot();
        }

        return new WorldSnapshot(snapshots, _tick);
    }

    public async ValueTask TickAsync(CancellationToken cancellationToken = default)
    {
        _tick++;
        foreach (var monster in Monsters())
        {
            if (!monster.IsAlive)
            {
                continue;
            }

            monster.ClearTaunts();
            await RunMonsterAsync(monster, cancellationToken).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<GameEntity> Players()
        => _entities.Where(e => e.Kind == EntityKind.Player).ToArray();

    public string Render()
    {
        var lines = _entities
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(e =>
                $"    {e.Id,-10} {e.Kind,-7} lvl={e.Level,-2} hp={e.Hp,-3} pos={e.Position}" +
                (e.IsAlive ? "" : " (defeated)"));
        return string.Join(Environment.NewLine, lines);
    }

    internal GameEntity? FindEntity(string id)
        => _entities.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    internal int GetHealth(string entityId)
        => FindEntity(entityId)?.Hp ?? 0;

    internal MonsterSnapshot GetMonsterSnapshot(string entityId)
    {
        if (FindEntity(entityId) is not { Kind: EntityKind.Monster } monster)
        {
            return new MonsterSnapshot(entityId, string.Empty, 0, 0, 0);
        }

        return new MonsterSnapshot(monster.Id, monster.Id, monster.Hp, monster.Level, monster.Position);
    }

    internal bool IsMonster(string entityId)
        => FindEntity(entityId)?.Kind == EntityKind.Monster;

    internal int GetLevel(string entityId)
        => FindEntity(entityId)?.Level ?? 0;

    internal int GetPosition(string entityId)
        => FindEntity(entityId)?.Position ?? 0;

    internal void SetPosition(string entityId, int position)
        => FindEntity(entityId)?.MoveTo(position);

    internal bool KillMonster(string monsterId)
    {
        if (FindEntity(monsterId) is not { Kind: EntityKind.Monster } monster)
        {
            return false;
        }

        return monster.Kill();
    }

    private async ValueTask RunMonsterAsync(GameEntity monster, CancellationToken cancellationToken)
    {
        var target = MonsterTargeting.SelectTarget(monster, Players());
        monster.SetTarget(target?.Id);
        if (target is null)
        {
            return;
        }

        var distance = Math.Abs(monster.Position - target.Position);
        if (distance <= AggroRange)
        {
            var aggro = new MonsterAggroEvent(monster.Id, target.Id, distance, monster.Level, target.Level);
            _subscriptions.Publish(aggro, cancellationToken);
            IndexRegistry.Publish(aggro, cancellationToken);

            // Plugins observe the aggro and may calm the monster for future ticks.
            await _hooks.PublishAsync(aggro, cancellationToken).ConfigureAwait(false);
        }

        MonsterTargeting.StepToward(monster, target.Position);

        var newDistance = Math.Abs(monster.Position - target.Position);
        if (newDistance <= 1 && target.IsAlive)
        {
            await AttackAsync(monster, target, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask AttackAsync(GameEntity monster, GameEntity target, CancellationToken cancellationToken)
    {
        var damage = Math.Max(1, monster.Level - target.Level);
        target.TakeDamage(damage);

        var attack = new AttackEvent(monster.Id, target.Id, damage, monster.Level);
        _subscriptions.Publish(attack, cancellationToken);
        IndexRegistry.Publish(attack, cancellationToken);

        // Plugins observe the attack and may taunt the attacker off the target.
        await _hooks.PublishAsync(attack, cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<GameEntity> Monsters()
        => _entities.Where(e => e.Kind == EntityKind.Monster);
}
