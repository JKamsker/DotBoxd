using DotBoxD.Kernels.Sandbox;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Game.Server.Abstractions;

public sealed record MonsterSnapshot(string Id, string Name, int Health, int Level, int Position);

/// <summary>
/// THE single game surface — one PURE domain interface, three consumers:
/// <list type="bullet">
///   <item>the <b>server</b> implements it for real (in-process world);</item>
///   <item>the <b>plugin</b> gets an RPC proxy generated for it (<c>[RpcService]</c>) — that proxy is the
///   <c>GamePluginServer</c> facade;</item>
///   <item>a <b>kernel</b> gets it injected; on the server its calls are local (no real async hop), but the
///   dev writes them exactly like the remote calls.</item>
/// </list>
/// <para><b>Pure domain on purpose.</b> Install recording (<c>Replace</c>/<c>Extend</c>) lives on the
/// generated builder's <c>Setup</c> accumulator, and live tuning (<c>Get</c>) lives on the facade, NOT here —
/// so a kernel that gets the world injected sees only domain reads, never an install verb that would throw.
/// <b>Routing is automatic</b>:
/// each method's identity is the binding/RPC route. The capability and host-state effect each call requires
/// are declared on this SDK contract with <c>[HostBinding]</c>, so the analyzer and runtime consume the
/// same metadata.</para>
/// </summary>
[RpcService]
public interface IGameWorldAccess
{
    /// <summary>Monster-specific commands and scoped monster handles exposed by the game world.</summary>
    IMonsterControl Monsters { get; }

    /// <summary>Entity-wide commands and scoped entity handles exposed by the game world.</summary>
    IEntityControl Entities { get; }
}

[RpcService]
public interface IMonsterControl
{
    /// <summary>
    /// A scoped <b>handle</b> for one monster. The id is captured here, so every call on the handle omits it —
    /// <c>Monsters.Get(id).KillAsync()</c>. This is a nested proxy: cheap and local (no I/O); the async hop
    /// happens when you call a method on the returned <see cref="IMonster"/>.
    /// </summary>
    [HostBinding("game.world.monster.read.handle", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    IMonster Get(string entityId);

    /// <summary>Whether the id currently belongs to a monster (collection-level classification).</summary>
    [HostBinding("game.world.monster.read.kind", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    ValueTask<bool> IsMonsterAsync(string entityId);
}

[RpcService]
public interface IEntityControl
{
    /// <summary>A scoped <b>handle</b> for one entity. The id is captured; calls on it omit it. No I/O.</summary>
    [HostBinding("game.world.entity.read.handle", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    IEntity Get(string entityId);
}

/// <summary>
/// An ENTITY handle — a domain surface scoped to a single id captured by <see cref="IEntityControl.Get"/> /
/// <see cref="IMonsterControl.Get"/>. Every call is id-implicit: <c>Entities.Get(id).GetHealthAsync()</c>.
/// </summary>
[RpcService]
public interface IEntity
{
    /// <summary>The id this handle is scoped to.</summary>
    string Id { get; }

    /// <summary>The entity's current hit points (0 if unknown or defeated).</summary>
    [HostBinding("game.world.entity.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    ValueTask<int> GetHealthAsync();

    /// <summary>The entity's level (0 if unknown).</summary>
    [HostBinding("game.world.entity.read.level", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    ValueTask<int> GetLevelAsync();

    /// <summary>The entity's 1D world position (0 if unknown).</summary>
    [HostBinding("game.world.entity.read.position", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    ValueTask<int> GetPositionAsync();
}

/// <summary>
/// A MONSTER handle — an <see cref="IEntity"/> plus monster-only behavior, all scoped to the captured id. A
/// plugin can graft instance-scoped extensions onto this type (<c>[ServerExtension(typeof(IMonster))]</c>) and
/// receive the monster injected, so it never re-specifies the id.
/// </summary>
[RpcService]
public interface IMonster : IEntity
{
    /// <summary>Immutable snapshot of this monster. An unknown/non-monster id yields an empty snapshot.</summary>
    [HostBinding("game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
    ValueTask<MonsterSnapshot> SnapshotAsync();

    /// <summary>Kills this monster and returns whether the world changed.</summary>
    [HostBinding("game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
    ValueTask<bool> KillAsync();

    /// <summary>This monster's combat threat rating (gated under its own capability subtree, server-side).</summary>
    [HostBinding("game.world.combat.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    ValueTask<int> GetThreatAsync();

    /// <summary>Moves this monster to a 1D world position.</summary>
    [HostBinding("game.world.monster.write.position", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
    ValueTask TeleportToAsync(int position);
}
