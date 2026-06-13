namespace SafeIR.Game.Server;

/// <summary>
/// The default policy the server applies to every untrusted kernel it runs. Grants logging and the
/// example-defined <c>host.message.write</c> capability, plus deterministic fuel and host-call
/// budgets. Without the message-write grant, package preparation fails closed.
/// </summary>
internal static class ServerPolicy
{
    public static SandboxPolicy Create()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();
}
