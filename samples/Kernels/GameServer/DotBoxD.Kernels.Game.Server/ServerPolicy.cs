using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Game.Server;

/// <summary>
/// Per-kernel least-privilege policies. Every event kernel gets logging, the example-defined
/// <c>host.message.write</c> capability, and deterministic fuel/host-call budgets. A kernel whose
/// analyzer-derived manifest declares a <c>game.world.monster.read.*</c> capability (the guardian's
/// gated <c>GetHealth</c>) additionally receives the matching wildcard grant; a kernel that does not
/// (the retaliation kernel) is not over-granted. The wildcard covers
/// <c>game.world.monster.read.health</c> but not <c>game.world.combat.threat</c> (<c>GetThreat</c>), so
/// a kernel that reads threat is denied at install. Without the message-write grant, package
/// preparation fails closed too.
/// Kernel RPC services get the same least-privilege treatment without the event-kernel message-write
/// base grant; the monster-killer batch kernel receives <c>game.world.monster.write.*</c> only because
/// its verified IR declares the kill binding.
/// </summary>
internal static class ServerPolicy
{
    private const string MonsterReadPrefix = "game.world.monster.read.";
    private const string MonsterWritePrefix = "game.world.monster.write.";

    /// <summary>The base ceiling applied to a kernel with no extra capability needs.</summary>
    public static SandboxPolicy Create() => ForKernel([]);

    /// <summary>
    /// Builds the policy granting exactly what a kernel's verified IR declares it needs. Grants are
    /// derived from the manifest's <see cref="DotBoxD.Plugins.PluginManifest.RequiredCapabilities"/> — the
    /// plugin cannot widen them, since the analyzer derived them from what the IR actually touches. Event
    /// kernels intentionally cannot receive monster-write grants; writes are reserved for RPC kernels.
    /// </summary>
    public static SandboxPolicy ForKernel(IReadOnlyList<string> requiredCapabilities)
        => ForRequiredCapabilities(
            requiredCapabilities,
            grantEventMessageWrite: true,
            grantMonsterWrite: false);

    /// <summary>Builds the policy for a kernel RPC service, without event-kernel base grants.</summary>
    public static SandboxPolicy ForRpcKernel(IReadOnlyList<string> requiredCapabilities)
        => ForRequiredCapabilities(
            requiredCapabilities,
            grantEventMessageWrite: false,
            grantMonsterWrite: true);

    private static SandboxPolicy ForRequiredCapabilities(
        IReadOnlyList<string> requiredCapabilities,
        bool grantEventMessageWrite,
        bool grantMonsterWrite)
    {
        var builder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000);

        if (grantEventMessageWrite || Requires(requiredCapabilities, PluginMessageBindings.CapabilityId))
        {
            builder.GrantHostMessageWrite();
        }

        if (Requires(requiredCapabilities, RuntimeCapabilityIds.Async))
        {
            builder.AllowRuntimeAsync();
        }

        if (requiredCapabilities.Any(capability =>
                capability.StartsWith(MonsterReadPrefix, StringComparison.Ordinal)))
        {
            builder.Grant("game.world.monster.read.*", new { }, SandboxEffect.HostStateRead);
        }

        if (grantMonsterWrite &&
            requiredCapabilities.Any(capability =>
                capability.StartsWith(MonsterWritePrefix, StringComparison.Ordinal)))
        {
            builder.Grant("game.world.monster.write.*", new { }, SandboxEffect.HostStateWrite);
        }

        return builder.Build();
    }

    private static bool Requires(IReadOnlyList<string> capabilities, string id)
        => capabilities.Contains(id, StringComparer.Ordinal);
}
