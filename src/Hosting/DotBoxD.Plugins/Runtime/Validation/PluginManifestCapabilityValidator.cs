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
        if (declared.Count == manifest.RequiredCapabilities.Count &&
            declared.SetEquals(expected))
        {
            return;
        }

        var missing = expected.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var extra = declared.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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
        var required = new HashSet<string>(
            plan.Module.CapabilityRequests.Select(request => request.Id),
            StringComparer.Ordinal);
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
}
