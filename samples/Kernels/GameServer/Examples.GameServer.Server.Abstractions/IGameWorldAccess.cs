using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Game.Server.Abstractions;

public sealed record MonsterSnapshot(string Id, string Name, int Health, int Level, int Position);

/// <summary>
/// THE single game surface — one PURE domain interface, three consumers:
/// <list type="bullet">
///   <item>the <b>server</b> implements it for real (in-process world);</item>
///   <item>the <b>plugin</b> gets an RPC proxy generated for it (<c>[DotBoxDService]</c>) — that proxy is the
///   <c>GamePluginServer</c> facade;</item>
///   <item>a <b>kernel</b> gets it injected; on the server its calls are local (no real async hop), but the
///   dev writes them exactly like the remote calls.</item>
/// </list>
/// <para><b>Pure domain on purpose.</b> The install verbs (<c>Replace</c>/<c>Extend</c>/<c>Get</c>) live on
/// the generated <c>GamePluginServer</c> facade and its control wrappers, NOT here — so a kernel that gets the
/// world injected sees only domain reads, never an install verb that would throw. <b>Routing is automatic</b>:
/// each method's identity is the binding/RPC route. The capability each call requires is declared on the
/// <b>server implementation</b> (see <c>GameWorldAccess</c>'s <c>[HostCapability]</c>); the read/write effect
/// is inferred from the impl. The contract stays a plain async interface.</para>
/// </summary>
[DotBoxDService]
public interface IGameWorldAccess
{
    IMonsterControl Monsters { get; }
    IEntityControl Entities { get; }
}

[DotBoxDService]
public interface IMonsterControl
{
    /// <summary>Immutable monster snapshot. Unknown or non-monster ids return an empty snapshot.</summary>
    ValueTask<MonsterSnapshot> GetAsync(string entityId);

    /// <summary>Kills a live monster by id and returns whether the world changed.</summary>
    ValueTask<bool> KillAsync(string entityId);

    /// <summary>Whether the id currently belongs to a monster.</summary>
    ValueTask<bool> IsMonsterAsync(string entityId);

    /// <summary>The entity's combat threat rating (gated under its own capability subtree, server-side).</summary>
    ValueTask<int> GetThreatAsync(string entityId);
}

[DotBoxDService]
public interface IEntityControl
{
    /// <summary>The entity's current hit points (0 if unknown or defeated).</summary>
    ValueTask<int> GetHealthAsync(string entityId);

    /// <summary>The entity's level (0 if unknown).</summary>
    ValueTask<int> GetLevelAsync(string entityId);

    /// <summary>The entity's 1D world position (0 if unknown).</summary>
    ValueTask<int> GetPositionAsync(string entityId);
}
