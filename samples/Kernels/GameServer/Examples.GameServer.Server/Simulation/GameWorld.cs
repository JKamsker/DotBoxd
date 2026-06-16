using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Game.Server.Simulation;

/// <summary>
/// A small deterministic simulation of players and monsters on a 1D integer line. There is no RNG:
/// given the same starting entities and the same plugin commands, every run produces identical
/// output. The world publishes <see cref="MonsterAggroEvent"/> and <see cref="AttackEvent"/> through
/// the plugin hook pipeline and applies any plugin commands the command sink recorded.
/// </summary>
internal sealed class GameWorld
{
    public const int AggroRange = 5;

    // A monster that has accumulated at least this much calm toward a player will hunt someone else.
    public const int CalmSuppressionThreshold = 30;

    private readonly List<GameEntity> _entities;
    private readonly HookRegistry _hooks;
    private int _tick;

    public GameWorld(IEnumerable<GameEntity> entities, HookRegistry hooks)
    {
        _entities = [.. entities];
        _hooks = hooks;
    }

    public int Tick => _tick;

    public static GameWorld CreateDefault(HookRegistry hooks)
        => new(
            [
                new GameEntity("player-1", EntityKind.Player, level: 1, hp: 30, position: 0),
                new GameEntity("player-2", EntityKind.Player, level: 3, hp: 30, position: 4),
                new GameEntity("monster-1", EntityKind.Monster, level: 8, hp: 80, position: 7),
                new GameEntity("monster-2", EntityKind.Monster, level: 8, hp: 80, position: 12),
                new GameEntity("monster-3", EntityKind.Monster, level: 6, hp: 55, position: 80),
                new GameEntity("monster-4", EntityKind.Monster, level: 6, hp: 45, position: 90)
            ],
            hooks);

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
            // Plugins observe the aggro and may calm the monster for future ticks.
            await _hooks.PublishAsync(
                    new MonsterAggroEvent(monster.Id, target.Id, distance, monster.Level, target.Level),
                    cancellationToken)
                .ConfigureAwait(false);
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

        // Plugins observe the attack and may taunt the attacker off the target.
        await _hooks.PublishAsync(
                new AttackEvent(monster.Id, target.Id, damage, monster.Level),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private IEnumerable<GameEntity> Monsters()
        => _entities.Where(e => e.Kind == EntityKind.Monster);
}
