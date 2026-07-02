using DotBoxD.Plugins.Indexing;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

/// <summary>Published whenever the authoritative gold ledger changes an entity balance.</summary>
public sealed record GoldChangedEvent(
    [property: EventIndexKey] string EntityId,
    int Balance,
    int Delta,
    string Reason);
