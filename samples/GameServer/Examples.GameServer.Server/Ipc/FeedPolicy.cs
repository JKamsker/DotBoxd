using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal static class FeedPolicy
{
    public static SandboxPolicy ForKernel(IReadOnlyList<string> requiredCapabilities)
    {
        var builder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(50_000)
            .WithMaxHostCalls(250);

        if (requiredCapabilities.Contains("host.message.write", StringComparer.Ordinal))
        {
            builder.GrantHostMessageWrite();
        }

        if (requiredCapabilities.Contains(RuntimeCapabilityIds.Async, StringComparer.Ordinal))
        {
            builder.AllowRuntimeAsync();
        }

        return builder.Build();
    }
}
