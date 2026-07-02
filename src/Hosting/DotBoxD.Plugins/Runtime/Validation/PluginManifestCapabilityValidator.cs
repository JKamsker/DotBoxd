using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Validation;

internal static class PluginManifestCapabilityValidator
{
    public static void Validate(
        PluginManifest manifest,
        SandboxModule module,
        ExecutionPlan plan,
        IReadOnlyList<string> entrypoints,
        List<SandboxDiagnostic> diagnostics,
        bool allowNonBindingCapabilities = true,
        bool includeModuleNonBindingCapabilities = true)
    {
        var declared = new HashSet<string>(manifest.RequiredCapabilities, StringComparer.Ordinal);
        var expected = RequiredCapabilities(plan, entrypoints);
        AddModuleNonBindingRequiredCapabilities(module, expected, includeModuleNonBindingCapabilities);
        var missing = expected
            .Except(declared, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var extra = declared
            .Except(expected, StringComparer.Ordinal)
            .Where(capability => !allowNonBindingCapabilities || !IsKnownNonBindingCapability(capability))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (declared.Count == manifest.RequiredCapabilities.Count &&
            missing.Length == 0 &&
            extra.Length == 0)
        {
            return;
        }

        var details = new List<string>();
        if (missing.Length > 0)
        {
            details.Add("missing: " + string.Join(", ", missing));
        }

        if (extra.Length > 0)
        {
            details.Add("extra: " + string.Join(", ", extra));
        }

        if (declared.Count != manifest.RequiredCapabilities.Count)
        {
            details.Add("duplicates present");
        }

        var message = details.Count == 0
            ? "Plugin manifest requiredCapabilities do not match verified entrypoint capabilities."
            : "Plugin manifest requiredCapabilities do not match verified entrypoint capabilities (" +
              string.Join("; ", details) +
              ").";
        diagnostics.Add(new SandboxDiagnostic(
            "DBXK044",
            message));
    }

    public static IEnumerable<string> NonBindingRequiredCapabilities(PluginManifest manifest)
        => manifest.RequiredCapabilities.Where(IsKnownNonBindingCapability);

    public static IEnumerable<string> NonBindingRequiredCapabilities(PluginManifest manifest, SandboxModule module)
        => manifest.RequiredCapabilities
            .Concat(ModuleNonBindingRequiredCapabilities(module))
            .Where(IsKnownNonBindingCapability);

    public static void ValidateRequiredCapabilityGrants(
        PluginManifest manifest,
        SandboxModule module,
        SandboxPolicy installPolicy,
        List<SandboxDiagnostic> diagnostics,
        bool includeModuleNonBindingCapabilities = true)
    {
        var now = installPolicy.GrantClock;
        var required = new HashSet<string>(manifest.RequiredCapabilities, StringComparer.Ordinal);
        AddModuleNonBindingRequiredCapabilities(module, required, includeModuleNonBindingCapabilities);
        foreach (var capability in required)
        {
            if (string.Equals(capability, RuntimeCapabilityIds.Async, StringComparison.Ordinal) ||
                installPolicy.GrantsCapability(capability, now))
            {
                continue;
            }

            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-CAP",
                $"required capability '{capability}' is not granted"));
        }
    }

    private static HashSet<string> RequiredCapabilities(ExecutionPlan plan, IReadOnlyList<string> entrypoints)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entrypoint in entrypoints)
        {
            required.UnionWith(plan.GetEntrypointMetadata(entrypoint).RequiredCapabilities);
        }

        return required;
    }

    private static void AddModuleNonBindingRequiredCapabilities(
        SandboxModule module,
        HashSet<string> capabilities,
        bool include)
    {
        if (!include)
        {
            return;
        }

        foreach (var capability in ModuleNonBindingRequiredCapabilities(module))
        {
            capabilities.Add(capability);
        }
    }

    public static bool IsKnownNonBindingCapability(string capability)
        => capability.StartsWith("event.read.", StringComparison.Ordinal);

    private static IEnumerable<string> ModuleNonBindingRequiredCapabilities(SandboxModule module)
    {
        if (!module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.RequiredCapabilities, out var metadata) ||
            string.IsNullOrWhiteSpace(metadata))
        {
            yield break;
        }

        foreach (var capability in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsKnownNonBindingCapability(capability))
            {
                yield return capability;
            }
        }
    }
}
