namespace SafeIR.Game.Server;

/// <summary>
/// Per-kernel least-privilege policies. Every kernel gets logging, the example-defined
/// <c>host.message.write</c> capability, and deterministic fuel/host-call budgets. A kernel whose
/// analyzer-derived manifest declares a <c>game.world.monster.read.*</c> capability (the guardian's
/// gated <c>GetHealth</c>) additionally receives the matching wildcard grant; a kernel that does not
/// (the retaliation kernel) is not over-granted. The wildcard covers
/// <c>game.world.monster.read.health</c> but not <c>game.world.combat.threat</c> (<c>GetThreat</c>), so
/// a kernel that reads threat is denied at install. Without the message-write grant, package
/// preparation fails closed too.
/// </summary>
internal static class ServerPolicy
{
    private const string MonsterReadPrefix = "game.world.monster.read.";

    /// <summary>The base ceiling applied to a kernel with no extra capability needs.</summary>
    public static SandboxPolicy Create() => ForKernel([]);

    /// <summary>
    /// Builds the policy granting exactly what a kernel's verified IR declares it needs. Grants are
    /// derived from the manifest's <see cref="SafeIR.Plugins.PluginManifest.RequiredCapabilities"/> — the
    /// plugin cannot widen them, since the analyzer derived them from what the IR actually touches.
    /// </summary>
    public static SandboxPolicy ForKernel(IReadOnlyList<string> requiredCapabilities)
    {
        var builder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000);

        if (requiredCapabilities.Any(capability =>
                capability.StartsWith(MonsterReadPrefix, StringComparison.Ordinal)))
        {
            builder.Grant("game.world.monster.read.*", new { }, SandboxEffect.HostStateRead);
        }

        return builder.Build();
    }
}
