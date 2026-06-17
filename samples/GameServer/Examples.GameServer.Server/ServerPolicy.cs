using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Game.Server;

/// <summary>
/// Per-kernel least-privilege policies. Every kernel gets logging, the example-defined
/// <c>host.message.write</c> capability, and deterministic fuel/host-call budgets. A kernel whose server-side
/// package analysis finds a <c>game.world.monster.read.*</c>, <c>game.world.entity.read.*</c>, or combat-read
/// capability additionally receives the matching grant; a kernel that does not (the retaliation kernel) is not
/// over-granted. Without the message-write grant, package preparation fails closed too. Server extensions get the
/// same least-privilege treatment; the monster-killer batch kernel receives <c>game.world.monster.write.*</c> only
/// because its verified IR declares the kill binding.
/// </summary>
internal static class ServerPolicy
{
    private const string MonsterReadPrefix = "game.world.monster.read.";
    private const string MonsterWritePrefix = "game.world.monster.write.";
    private const string EntityReadPrefix = "game.world.entity.read.";
    private const string CombatThreat = "game.world.combat.threat";

    /// <summary>The base ceiling applied to a kernel with no extra capability needs.</summary>
    public static SandboxPolicy Create() => ForKernel([]);

    /// <summary>
    /// Builds the policy granting exactly what server-side package analysis says the verified IR needs.
    /// </summary>
    public static SandboxPolicy ForKernel(IReadOnlyList<string> requiredCapabilities)
    {
        var builder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000);

        if (RequiresPrefix(requiredCapabilities, MonsterReadPrefix))
        {
            builder.Grant("game.world.monster.read.*", new { }, SandboxEffect.HostStateRead);
        }

        if (RequiresPrefix(requiredCapabilities, EntityReadPrefix))
        {
            builder.Grant("game.world.entity.read.*", new { }, SandboxEffect.HostStateRead);
        }

        if (RequiresPrefix(requiredCapabilities, MonsterWritePrefix))
        {
            builder.Grant("game.world.monster.write.*", new { }, SandboxEffect.HostStateWrite);
        }

        if (requiredCapabilities.Contains(CombatThreat, StringComparer.Ordinal))
        {
            builder.Grant(CombatThreat, new { }, SandboxEffect.HostStateRead);
        }

        return builder.Build();
    }

    private static bool RequiresPrefix(IReadOnlyList<string> requiredCapabilities, string prefix)
        => requiredCapabilities.Any(capability => capability.StartsWith(prefix, StringComparison.Ordinal));
}
