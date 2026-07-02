namespace DotBoxD.Kernels.Game.Client.Abstractions.Events;

public sealed record ClientGoldChangedEvent(string EntityId, int Balance, int Delta);
