namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

[Hook("game.damage.decision", typeof(RemoteDamageDecisionResult))]
public sealed record RemoteDamageDecisionEvent(string MonsterId, int Damage);

[HookResult]
public readonly partial record struct RemoteDamageDecisionResult(
    bool Success,
    string? Reason,
    int Damage) : IHookResult;
