using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Runtime.Validation;

namespace DotBoxD.Plugins;

using DotBoxD.Kernels;

public sealed partial class PluginServer
{
    private (SandboxModule Module, SandboxPolicy Policy) PrepareSandboxInputs(
        PluginPackage package,
        SandboxPolicy installPolicy)
    {
        var includeNonBindingCapabilities = package.Manifest.RpcEntrypoint is null;
        ValidatePluginCapabilityRequests(package.Module, installPolicy, includeNonBindingCapabilities);
        var nonBindingCapabilities = includeNonBindingCapabilities
            ? PluginManifestCapabilityValidator
                .NonBindingRequiredCapabilities(package.Manifest, package.Module)
                .Concat(NonBindingCapabilityRequests(package.Module))
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        ValidateNonBindingCapabilities(package.Manifest, installPolicy, nonBindingCapabilities);
        var discoveryModule = package.Module with
        {
            CapabilityRequests = includeNonBindingCapabilities
                ? package.Module.CapabilityRequests
                    .Where(static request => !PluginManifestCapabilityValidator.IsKnownNonBindingCapability(request.Id))
                    .ToArray()
                : package.Module.CapabilityRequests
        };
        var requiredCapabilities = _host.GetRequiredCapabilities(discoveryModule, installPolicy)
            .ToHashSet(StringComparer.Ordinal);
        requiredCapabilities.UnionWith(nonBindingCapabilities);
        var requestedCapabilities = package.Module.CapabilityRequests
            .Select(static request => request.Id)
            .ToHashSet(StringComparer.Ordinal);
        var grants = installPolicy.Grants
            .Where(grant =>
                MatchesRequiredCapability(grant.Id, requiredCapabilities) ||
                MatchesRequiredCapability(grant.Id, requestedCapabilities))
            .ToArray();
        var runtimeRequests = nonBindingCapabilities
            .Order(StringComparer.Ordinal)
            .Select(static capability => new CapabilityRequest(capability, "non-binding runtime capability"))
            .ToArray();
        return (package.Module with { CapabilityRequests = runtimeRequests }, installPolicy with { Grants = grants });
    }

    private static void ValidatePluginCapabilityRequests(
        SandboxModule module,
        SandboxPolicy installPolicy,
        bool skipNonBindingCapabilities)
    {
        if (module.CapabilityRequests.Count == 0)
        {
            return;
        }

        var now = installPolicy.GrantClock;
        var diagnostics = new List<SandboxDiagnostic>();
        foreach (var request in module.CapabilityRequests)
        {
            if (skipNonBindingCapabilities &&
                PluginManifestCapabilityValidator.IsKnownNonBindingCapability(request.Id))
            {
                continue;
            }

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

    private static IEnumerable<string> NonBindingCapabilityRequests(SandboxModule module)
        => module.CapabilityRequests
            .Select(static request => request.Id)
            .Where(PluginManifestCapabilityValidator.IsKnownNonBindingCapability);

    private static void ValidateNonBindingCapabilities(
        PluginManifest manifest,
        SandboxPolicy installPolicy,
        IReadOnlySet<string> nonBindingCapabilities)
    {
        if (nonBindingCapabilities.Count == 0)
        {
            return;
        }

        var diagnostics = new List<SandboxDiagnostic>();
        var declared = new HashSet<string>(manifest.RequiredCapabilities, StringComparer.Ordinal);
        var missing = nonBindingCapabilities
            .Except(declared, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK044",
                "Plugin manifest requiredCapabilities do not match verified entrypoint capabilities " +
                $"(missing: {string.Join(", ", missing)})."));
        }

        var now = installPolicy.GrantClock;
        foreach (var capability in nonBindingCapabilities.Order(StringComparer.Ordinal))
        {
            if (!installPolicy.GrantsCapability(capability, now))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-CAP",
                    $"required capability '{capability}' is not granted"));
            }
        }

        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }
    }
}
