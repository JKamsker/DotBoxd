namespace SafeIR.Game.Server;

/// <summary>
/// The extended capability defined by this example. The plugin's only sandbox grant is
/// <c>host.message.write</c>; the meaning of those messages is defined HERE, not in SafeIR core.
/// This sink parses the command DSL, validates it (known verb, known/opaque entity ids, clamped
/// strength), and applies it to the <see cref="GameWorld"/>. Unknown or invalid commands are ignored
/// safely and never throw back into the sandbox.
/// </summary>
internal sealed class GameCommandSink : IPluginMessageSink
{
    private const int MaxCalmStrength = 50;

    private readonly object _gate = new();
    private readonly List<string> _effects = [];
    private GameWorld? _world;

    /// <summary>Bound after the world is built (the world needs the hooks, the sink needs the world).</summary>
    public void Bind(GameWorld world) => _world = world;

    public void Send(string targetId, string message) => Apply(targetId, message);

    public ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Apply(targetId, message);
        return ValueTask.CompletedTask;
    }

    public string[] DrainEffects()
    {
        lock (_gate)
        {
            var drained = _effects.ToArray();
            _effects.Clear();
            return drained;
        }
    }

    private void Apply(string targetId, string message)
    {
        var world = _world;
        if (world is null || !GameCommands.TryParse(message, out var command))
        {
            return;
        }

        var monster = world.FindEntity(targetId);
        switch (command.Verb)
        {
            case CommandVerb.Calm:
                ApplyCalm(world, monster, command);
                break;
            case CommandVerb.Taunt:
                ApplyTaunt(world, monster, command);
                break;
            default:
                break;
        }
    }

    private void ApplyCalm(GameWorld world, GameEntity? monster, GameCommand command)
    {
        // Validate: target must be a known monster, the player must exist, strength clamped.
        if (monster is null || monster.Kind != EntityKind.Monster)
        {
            return;
        }

        if (world.FindEntity(command.Argument) is not { Kind: EntityKind.Player })
        {
            return;
        }

        var strength = Math.Clamp(command.Strength, 0, MaxCalmStrength);
        if (strength == 0)
        {
            return;
        }

        monster.AddCalm(command.Argument, strength);
        Record($"calm: {monster.Id} soothed toward {command.Argument} (+{strength}, total {monster.CalmTowards(command.Argument)})");
    }

    private void ApplyTaunt(GameWorld world, GameEntity? attacker, GameCommand command)
    {
        // Validate: target must be a known monster (the attacker) and the original target must exist.
        if (attacker is null || attacker.Kind != EntityKind.Monster)
        {
            return;
        }

        if (world.FindEntity(command.Argument) is not { Kind: EntityKind.Player })
        {
            return;
        }

        attacker.TauntAwayFrom(command.Argument);
        Record($"taunt: {attacker.Id} pulled off {command.Argument} this turn");
    }

    private void Record(string effect)
    {
        lock (_gate)
        {
            _effects.Add(effect);
        }
    }
}
