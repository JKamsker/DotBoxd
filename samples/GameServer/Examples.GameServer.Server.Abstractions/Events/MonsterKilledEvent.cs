using DotBoxD.Plugins.Indexing;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

/// <summary>Published once when a monster reaches zero health.</summary>
public sealed record MonsterKilledEvent(
    [property: EventIndexKey] string MonsterId,
    [property: EventIndexKey] string KillerId,
    int Level,
    int Tick);
