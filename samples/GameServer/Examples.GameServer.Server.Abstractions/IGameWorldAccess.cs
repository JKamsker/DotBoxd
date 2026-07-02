using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Game.Server.Abstractions;

public sealed record MonsterSnapshot(string Id, string Name, int Health, int Level, int Position);

public sealed record PlayerSnapshot(string Id, int Level, int Health, int Gold);

/// <summary>Read-only world surface exposed to the trusted vendor client over TCP.</summary>
[DotBoxDService]
public interface IGameWorldView
{
    ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default);

    ValueTask<int> GetBalanceAsync(string entityId, CancellationToken ct = default);
}

/// <summary>
/// THE single game surface — one PURE domain interface, three consumers:
/// <list type="bullet">
///   <item>the <b>server</b> implements it for real (in-process world);</item>
///   <item>the <b>plugin</b> gets an RPC proxy generated for it (<c>[DotBoxDService]</c>) — that proxy is the
///   <c>GamePluginServer</c> facade;</item>
///   <item>a <b>kernel</b> gets it injected; on the server its calls are local (no real async hop), but the
///   dev writes them exactly like the remote calls.</item>
/// </list>
/// <para><b>Pure domain on purpose.</b> Install recording (<c>Replace</c>/<c>Extend</c>) lives on the
/// generated builder's <c>Setup</c> accumulator, and live tuning (<c>Get</c>) lives on the facade, NOT here —
/// so a kernel that gets the world injected sees only domain reads, never an install verb that would throw.
/// <b>Routing is automatic</b>:
/// each method's identity is the binding/RPC route. The capability and host-state effect each call requires
/// are declared on this SDK contract with <c>[HostCapability]</c>, so the analyzer and runtime consume the
/// same metadata.</para>
/// </summary>
[DotBoxDService]
public interface IGameWorldAccess
{
    /// <summary>Monster-specific commands and scoped monster handles exposed by the game world.</summary>
    IMonsterControl Monsters { get; }

    /// <summary>Entity-wide commands and scoped entity handles exposed by the game world.</summary>
    IEntityControl Entities { get; }

    /// <summary>Privileged gold-economy bindings exposed only inside the server-side sandbox.</summary>
    IGoldLedger Gold { get; }
}

[DotBoxDService]
public interface IGoldLedger
{
    [HostCapability("game.world.gold.read.balance", HostBindingEffect.HostStateRead)]
    ValueTask<int> GetBalanceAsync(string entityId);

    [HostCapability("game.world.gold.read.claimable", HostBindingEffect.HostStateRead)]
    ValueTask<bool> IsBountyClaimableAsync(string monsterId);

    [HostCapability("game.world.gold.write.grant", HostBindingEffect.HostStateWrite)]
    ValueTask<bool> GrantBountyAsync(string playerId, string monsterId, int amount);
}

[DotBoxDService]
public interface IMonsterControl
{
    /// <summary>
    /// A scoped <b>handle</b> for one monster. The id is captured here, so every call on the handle omits it —
    /// <c>Monsters.Get(id).KillAsync()</c>. This is a nested proxy: cheap and local (no I/O); the async hop
    /// happens when you call a method on the returned <see cref="IMonster"/>.
    /// </summary>
    [HostCapability("game.world.monster.read.handle", HostBindingEffect.HostStateRead)]
    IMonster Get(string entityId);

    /// <summary>Whether the id currently belongs to a monster (collection-level classification).</summary>
    [HostCapability("game.world.monster.read.kind", HostBindingEffect.HostStateRead)]
    ValueTask<bool> IsMonsterAsync(string entityId);
}

[DotBoxDService]
public interface IEntityControl
{
    /// <summary>A scoped <b>handle</b> for one entity. The id is captured; calls on it omit it. No I/O.</summary>
    [HostCapability("game.world.entity.read.handle", HostBindingEffect.HostStateRead)]
    IEntity Get(string entityId);
}

/// <summary>
/// An ENTITY handle — a domain surface scoped to a single id captured by <see cref="IEntityControl.Get"/> /
/// <see cref="IMonsterControl.Get"/>. Every call is id-implicit: <c>Entities.Get(id).GetHealthAsync()</c>.
/// </summary>
[DotBoxDService]
public interface IEntity
{
    /// <summary>The id this handle is scoped to.</summary>
    string Id { get; }

    /// <summary>The entity's current hit points (0 if unknown or defeated).</summary>
    [HostCapability("game.world.entity.read.health", HostBindingEffect.HostStateRead)]
    ValueTask<int> GetHealthAsync();

    /// <summary>The entity's level (0 if unknown).</summary>
    [HostCapability("game.world.entity.read.level", HostBindingEffect.HostStateRead)]
    ValueTask<int> GetLevelAsync();

    /// <summary>The entity's 1D world position (0 if unknown).</summary>
    [HostCapability("game.world.entity.read.position", HostBindingEffect.HostStateRead)]
    ValueTask<int> GetPositionAsync();
}

/// <summary>
/// A MONSTER handle — an <see cref="IEntity"/> plus monster-only behavior, all scoped to the captured id. A
/// plugin can graft instance-scoped extensions onto this type (<c>[ServerExtension(typeof(IMonster))]</c>) and
/// receive the monster injected, so it never re-specifies the id.
/// </summary>
[DotBoxDService]
public interface IMonster : IEntity
{
    /// <summary>Immutable snapshot of this monster. An unknown/non-monster id yields an empty snapshot.</summary>
    [HostCapability("game.world.monster.read.snapshot", HostBindingEffect.HostStateRead | HostBindingEffect.Allocates)]
    ValueTask<MonsterSnapshot> SnapshotAsync();

    /// <summary>Kills this monster and returns whether the world changed.</summary>
    [HostCapability("game.world.monster.write.kill", HostBindingEffect.HostStateWrite)]
    ValueTask<bool> KillAsync();

    /// <summary>This monster's combat threat rating (gated under its own capability subtree, server-side).</summary>
    [HostCapability("game.world.combat.threat", HostBindingEffect.HostStateRead)]
    ValueTask<int> GetThreatAsync();

    /// <summary>Moves this monster to a 1D world position.</summary>
    [HostCapability("game.world.monster.write.position", HostBindingEffect.HostStateWrite)]
    ValueTask TeleportToAsync(int position);
}
