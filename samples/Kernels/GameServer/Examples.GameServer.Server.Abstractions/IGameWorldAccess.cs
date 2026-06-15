using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Game.Server.Abstractions;

using DotBoxD.Kernels;

public sealed record MonsterSnapshot(string Id, string Name, int Health, int Level, int Position);

/// <summary>
/// Gated host-world access a kernel reaches through <c>ctx.Host&lt;IGameWorldAccess&gt;()</c>. Each method
/// is a sandbox binding (<see cref="HostBindingAttribute"/>): the DotBoxD.Kernels generator lowers the call to
/// the binding id and records its capability in the manifest's required capabilities, so a kernel only
/// installs under a policy that grants it. The server registers a matching binding (same id + capability)
/// backed by the live world.
/// </summary>
public interface IGameWorldAccess
{
    /// <summary>Immutable monster snapshot. Unknown or non-monster ids return an empty snapshot.</summary>
    [HostBinding("host.world.getMonster", "game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
    MonsterSnapshot GetMonster(string entityId);

    /// <summary>The entity's current hit points (0 if unknown or defeated). Monster-read capability.</summary>
    [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int GetHealth(string entityId);

    /// <summary>Whether the id currently belongs to a monster. Monster-read capability.</summary>
    [HostBinding("host.world.isMonster", "game.world.monster.read.kind", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    bool IsMonster(string entityId);

    /// <summary>The entity's level (0 if unknown). Monster-read capability.</summary>
    [HostBinding("host.world.getLevel", "game.world.monster.read.level", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int GetLevel(string entityId);

    /// <summary>The entity's 1D world position (0 if unknown). Monster-read capability.</summary>
    [HostBinding("host.world.getPosition", "game.world.monster.read.position", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int GetPosition(string entityId);

    /// <summary>Kills a live monster by id and returns whether the world changed. Monster-write capability.</summary>
    [HostBinding("host.world.killMonster", "game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
    bool KillMonster(string entityId);

    /// <summary>
    /// The entity's combat threat rating. Also a read, but gated under a different capability subtree
    /// (<c>game.world.combat.*</c>) than the monster-read grant: a kernel that reads it is denied at
    /// install unless that subtree is granted, which the guardian is not.
    /// </summary>
    [HostBinding("host.world.getThreat", "game.world.combat.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int GetThreat(string entityId);
}
