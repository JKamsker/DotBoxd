namespace SafeIR.Game.Server.Abstractions;

using SafeIR;

/// <summary>
/// Gated host-world access a kernel reaches through <c>ctx.Host&lt;IGameWorldAccess&gt;()</c>. Each method
/// is a sandbox binding (<see cref="HostBindingAttribute"/>): the SafeIR generator lowers the call to
/// the binding id and records its capability in the manifest's required capabilities, so a kernel only
/// installs under a policy that grants it. The server registers a matching binding (same id + capability)
/// backed by the live world.
/// </summary>
public interface IGameWorldAccess
{
    /// <summary>The entity's current hit points (0 if unknown or defeated). Monster-read capability.</summary>
    [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int GetHealth(string entityId);

    /// <summary>
    /// The entity's combat threat rating. Also a read, but gated under a different capability subtree
    /// (<c>game.world.combat.*</c>) than the monster-read grant: a kernel that reads it is denied at
    /// install unless that subtree is granted, which the guardian is not.
    /// </summary>
    [HostBinding("host.world.getThreat", "game.world.combat.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int GetThreat(string entityId);
}
