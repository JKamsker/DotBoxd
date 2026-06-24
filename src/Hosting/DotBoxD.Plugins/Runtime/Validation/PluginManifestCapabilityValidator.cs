using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Validation;

using DotBoxD.Kernels;

internal static class PluginManifestCapabilityValidator
{
    public static void Validate(
        PluginManifest manifest,
        ExecutionPlan plan,
        IReadOnlyList<string> entrypoints,
        List<SandboxDiagnostic> diagnostics)
    {
        var declared = new HashSet<string>(manifest.RequiredCapabilities, StringComparer.Ordinal);
        var expected = RequiredCapabilities(plan, entrypoints);
        var comparableDeclared = declared
            .Where(capability => expected.Contains(capability) || IsKnownSandboxCapability(capability, plan))
            .ToHashSet(StringComparer.Ordinal);
        if (declared.Count == manifest.RequiredCapabilities.Count &&
            comparableDeclared.SetEquals(expected))
        {
            return;
        }

        var missing = expected
            .Except(comparableDeclared, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var extra = comparableDeclared
            .Except(expected, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
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

    private static HashSet<string> RequiredCapabilities(ExecutionPlan plan, IReadOnlyList<string> entrypoints)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entrypoint in entrypoints)
        {
            if (!plan.BindingReferences.TryGetValue(entrypoint, out var references))
            {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (!plan.Bindings.TryGet(bindingId, out var binding))
                {
                    continue;
                }

                if (binding.RequiredCapability is not null)
                {
                    required.Add(binding.RequiredCapability);
                }

                if (binding.IsAsync || (binding.Effects & SandboxEffect.Concurrency) != 0)
                {
                    required.Add(RuntimeCapabilityIds.Async);
                }
            }
        }

        return required;
    }

    private static bool IsKnownSandboxCapability(string capability, ExecutionPlan plan)
    {
        if (capability is "file.read" or "file.write" or "time.now" or "random" or "log.write" ||
            string.Equals(capability, RuntimeCapabilityIds.Async, StringComparison.Ordinal) ||
            string.Equals(capability, RuntimeCapabilityIds.Reentrant, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var binding in plan.Bindings.Signatures)
        {
            if (string.Equals(binding.RequiredCapability, capability, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
