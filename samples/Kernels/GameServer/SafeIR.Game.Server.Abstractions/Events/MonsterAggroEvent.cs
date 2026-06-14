namespace SafeIR.Game.Server.Abstractions;

/// <summary>
/// Published when a monster detects a player within sensing range. Plugins subscribe to decide
/// whether to calm the monster so weak players are not bullied. No hand-written event adapter is
/// needed — the framework infers the sandbox shape from the record's properties (parameter names
/// <c>e_&lt;PropertyName&gt;</c>, CLR types mapped to sandbox types).
/// </summary>
public sealed record MonsterAggroEvent(
    string MonsterId,
    string PlayerId,
    int Distance,
    int MonsterLevel,
    int PlayerLevel);
