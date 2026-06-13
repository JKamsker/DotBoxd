namespace SafeIR.Game.PluginHost;

/// <summary>
/// Sandbox policy for the in-process local preview. Grants only what the kernels need:
/// logging and the example-defined <c>host.message.write</c> capability, plus fuel and host-call
/// budgets. The real server applies its own policy when running the shipped IR.
/// </summary>
internal static class PluginHostPolicy
{
    public static SandboxPolicy Create()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();
}
