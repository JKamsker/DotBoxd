using DotBoxD.Kernels.Policies;

namespace DotBoxD.Plugins;

using DotBoxD.Kernels;

public sealed partial class PluginServer
{
    private (SandboxModule Module, SandboxPolicy Policy) PrepareSandboxInputs(
        PluginPackage package,
        SandboxPolicy installPolicy)
    {
        var requiredCapabilities = _host.GetRequiredCapabilities(package.Module)
            .ToHashSet(StringComparer.Ordinal);
        var grants = installPolicy.Grants
            .Where(grant => MatchesRequiredCapability(grant.Id, requiredCapabilities))
            .ToArray();
        return (package.Module with { CapabilityRequests = [] }, installPolicy with { Grants = grants });
    }

    private static bool MatchesRequiredCapability(
        string grantId,
        IReadOnlySet<string> requiredCapabilities)
    {
        foreach (var capability in requiredCapabilities)
        {
            if (string.Equals(grantId, capability, StringComparison.Ordinal) ||
                CapabilityPattern.Matches(grantId, capability))
            {
                return true;
            }
        }

        return false;
    }
}
