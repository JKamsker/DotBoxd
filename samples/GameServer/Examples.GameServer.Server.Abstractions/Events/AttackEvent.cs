namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

/// <summary>
/// Published when a monster attacks an adjacent player. Plugins subscribe to taunt strong attackers
/// away from the target they are bullying. No hand-written event adapter is needed — the framework
/// infers the sandbox shape from the record's properties.
/// </summary>
public sealed record AttackEvent(
    string AttackerId,
    string TargetId,
    int Damage,
    int AttackerLevel);
