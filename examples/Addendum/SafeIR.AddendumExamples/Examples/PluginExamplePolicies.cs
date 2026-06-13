namespace SafeIR.AddendumExamples.Examples;

using SafeIR;

internal static class PluginExamplePolicies
{
    public static SandboxPolicy MessageWrite()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantGameMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();
}
