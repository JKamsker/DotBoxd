using DotBoxD.Plugins.Indexing;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

/// <summary>
/// Published when a monster attacks an adjacent player. Plugins subscribe to taunt strong attackers
/// away from the target they are bullying. No hand-written event adapter is needed — the framework
/// infers the sandbox shape from the record's properties.
/// <para>
/// <see cref="AttackerId"/>, <see cref="TargetId"/>, and <see cref="Damage"/> are marked
/// <see cref="EventIndexKeyAttribute"/>: the host keeps dispatch indexes for them, so a lowered
/// <c>.Where(e =&gt; e.AttackerId == "player-1" &amp;&amp; e.Damage &gt;= 5)</c> can be prefiltered through
/// equality/range buckets before the verified IR runs. <see cref="AttackerLevel"/> is intentionally
/// un-indexed, so predicates over it stay verified-IR only.
/// </para>
/// </summary>
public sealed record AttackEvent(
    [property: EventIndexKey] string AttackerId,
    [property: EventIndexKey] string TargetId,
    [property: EventIndexKey] int Damage,
    int AttackerLevel);
