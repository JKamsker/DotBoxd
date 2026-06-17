namespace DotBoxD.Kernels.Game.Server.Simulation;

/// <summary>
/// Deterministic target selection and movement for monsters. Calm and taunt effects (recorded by
/// the plugin command sink) steer the monster away from players it would otherwise bully.
/// </summary>
internal static class MonsterTargeting
{
    /// <summary>
    /// Picks the nearest living player the monster is neither calmed away from nor taunted off.
    /// Ties break on the player id so runs stay reproducible.
    /// </summary>
    public static GameEntity? SelectTarget(GameEntity monster, IReadOnlyList<GameEntity> players)
    {
        GameEntity? best = null;
        var bestDistance = int.MaxValue;
        foreach (var player in players)
        {
            if (!player.IsAlive)
            {
                continue;
            }

            if (monster.CalmTowards(player.Id) >= GameWorld.CalmSuppressionThreshold ||
                monster.IsTauntedAwayFrom(player.Id))
            {
                continue;
            }

            var distance = Math.Abs(monster.Position - player.Position);
            if (distance < bestDistance ||
                (distance == bestDistance && IsLowerId(player, best)))
            {
                best = player;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static void StepToward(GameEntity monster, int targetPosition)
    {
        if (monster.Position < targetPosition)
        {
            monster.MoveTo(monster.Position + 1);
        }
        else if (monster.Position > targetPosition)
        {
            monster.MoveTo(monster.Position - 1);
        }
    }

    private static bool IsLowerId(GameEntity candidate, GameEntity? current)
        => current is null ||
           string.CompareOrdinal(candidate.Id, current.Id) < 0;
}
