namespace DotBoxD.Kernels.Game.Client.Abstractions.Events;

public sealed record ClientMonsterKilledEvent(string MonsterId, string KillerId, int Level);
