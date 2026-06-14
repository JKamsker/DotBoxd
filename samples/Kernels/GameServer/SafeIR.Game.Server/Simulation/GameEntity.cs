namespace SafeIR.Game.Server;

internal enum EntityKind
{
    Player,
    Monster
}

/// <summary>
/// A single simulated entity on the 1D integer line. Monsters additionally track the player they are
/// hunting and a per-player "calm" modifier that plugin commands raise to keep them off weak players.
/// </summary>
internal sealed class GameEntity
{
    public GameEntity(string id, EntityKind kind, int level, int hp, int position)
    {
        Id = id;
        Kind = kind;
        Level = level;
        Hp = hp;
        Position = position;
    }

    public string Id { get; }
    public EntityKind Kind { get; }
    public int Level { get; }
    public int Hp { get; private set; }
    public int Position { get; private set; }
    public string? TargetId { get; private set; }

    public bool IsAlive => Hp > 0;

    /// <summary>Per-player calm strength applied to this monster (player id -&gt; accumulated calm).</summary>
    private readonly Dictionary<string, int> _calmByPlayer = new(StringComparer.Ordinal);

    /// <summary>Players this monster has been taunted away from and will skip this turn.</summary>
    private readonly HashSet<string> _tauntedAway = new(StringComparer.Ordinal);

    public void MoveTo(int position) => Position = position;

    public void SetTarget(string? targetId) => TargetId = targetId;

    public void TakeDamage(int amount) => Hp = Math.Max(0, Hp - amount);

    public void AddCalm(string playerId, int strength)
    {
        var current = _calmByPlayer.GetValueOrDefault(playerId);
        _calmByPlayer[playerId] = Math.Min(100, current + strength);
    }

    public int CalmTowards(string playerId) => _calmByPlayer.GetValueOrDefault(playerId);

    public void TauntAwayFrom(string playerId) => _tauntedAway.Add(playerId);

    public bool IsTauntedAwayFrom(string playerId) => _tauntedAway.Contains(playerId);

    public void ClearTaunts() => _tauntedAway.Clear();

    public EntitySnapshot ToSnapshot()
        => new(Id, Kind.ToString(), Level, Hp, Position);
}
