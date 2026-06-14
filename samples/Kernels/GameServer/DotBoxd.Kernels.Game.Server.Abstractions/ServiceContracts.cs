namespace DotBoxd.Kernels.Game.Server.Abstractions;

/// <summary>
/// Server-published service contracts. A plugin kernel implements one of these to declare its
/// behavior as a named domain service rather than a bare <c>IEventKernel&lt;TEvent&gt;</c>. Each extends
/// the framework <c>IEventKernel&lt;TEvent&gt;</c>, so the analyzer detects the event transitively (no
/// analyzer change) and the server wires the kernel by its subscribed event.
/// </summary>
public interface IMonsterAggroService : IEventKernel<MonsterAggroEvent>
{
}

/// <summary>Server contract for kernels that react to a monster attacking a player.</summary>
public interface IAttackService : IEventKernel<AttackEvent>
{
}
