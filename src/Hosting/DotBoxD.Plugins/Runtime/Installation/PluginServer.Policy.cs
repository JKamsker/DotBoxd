using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Plugins;

using DotBoxD.Kernels;

public sealed partial class PluginServer
{
    private (SandboxModule Module, SandboxPolicy Policy) PrepareSandboxInputs(
        PluginPackage package,
        SandboxPolicy installPolicy)
    {
        ValidatePluginCapabilityRequests(package.Module, installPolicy);
        var requiredCapabilities = _host.GetRequiredCapabilities(package.Module, installPolicy)
            .ToHashSet(StringComparer.Ordinal);
        var requestedCapabilities = package.Module.CapabilityRequests
            .Select(static request => request.Id)
            .ToHashSet(StringComparer.Ordinal);
        var grants = installPolicy.Grants
            .Where(grant =>
                MatchesRequiredCapability(grant.Id, requiredCapabilities) ||
                MatchesRequiredCapability(grant.Id, requestedCapabilities))
            .ToArray();
        return (package.Module with { CapabilityRequests = [] }, installPolicy with { Grants = grants });
    }

    private static void ValidatePluginCapabilityRequests(
        SandboxModule module,
        SandboxPolicy installPolicy)
    {
        if (module.CapabilityRequests.Count == 0)
        {
            return;
        }

        var now = installPolicy.GrantClock;
        var diagnostics = new List<SandboxDiagnostic>();
        foreach (var request in module.CapabilityRequests)
        {
            if (!installPolicy.GrantsCapability(request.Id, now))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-CAP",
                    $"requested capability '{request.Id}' is not granted"));
            }
        }

        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }
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
