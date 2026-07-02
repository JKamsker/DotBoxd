using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Game.Client.Sandbox;

internal static class ClientPolicy
{
    public static SandboxPolicy ForKernel(IReadOnlyList<string> requiredCapabilities)
    {
        var builder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(50_000)
            .WithMaxHostCalls(250);

        GrantIfRequired(builder, requiredCapabilities, "game.client.ui.write", SandboxEffect.HostStateWrite);
        GrantIfRequired(builder, requiredCapabilities, "game.client.fx.play", SandboxEffect.HostStateWrite);
        GrantIfRequired(
            builder,
            requiredCapabilities,
            "game.client.server.call",
            SandboxEffect.HostStateWrite | SandboxEffect.Alloc);

        if (requiredCapabilities.Contains(RuntimeCapabilityIds.Async, StringComparer.Ordinal))
        {
            builder.AllowRuntimeAsync();
        }

        return builder.Build();
    }

    private static void GrantIfRequired(
        SandboxPolicyBuilder builder,
        IReadOnlyList<string> requiredCapabilities,
        string capability,
        SandboxEffect effect)
    {
        if (requiredCapabilities.Contains(capability, StringComparer.Ordinal))
        {
            builder.Grant(capability, new { }, effect);
        }
    }
}
