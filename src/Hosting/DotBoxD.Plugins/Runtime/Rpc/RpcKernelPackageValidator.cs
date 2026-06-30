using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Lifecycle;
using DotBoxD.Plugins.Runtime.Validation;

namespace DotBoxD.Plugins.Runtime.Rpc;

/// <summary>
/// Validates a <b>server extension</b> package — a kernel invoked request/response with caller
/// arguments whose result is returned to the host (one server-side roundtrip), rather than dispatched
/// through the event <c>ShouldHandle</c>/<c>Handle</c> contract. It reuses the generic manifest checks
/// (ids, metadata, effects, live settings) but replaces the event-specific rules (a non-empty
/// subscription list, an <c>IEventKernel&lt;TEvent&gt;</c> contract, the two fixed entrypoints) with a
/// single <see cref="PluginManifest.RpcEntrypoint"/> that must resolve to a public entrypoint whose
/// trailing parameters are exactly the manifest's live settings.
/// </summary>
internal static class RpcKernelPackageValidator
{
    public static void Validate(PluginPackage package)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        PluginManifestTextValidator.ValidatePluginId(package.Manifest.PluginId, diagnostics);
        PluginManifestTextValidator.ValidateText(package.Manifest.Contract, "plugin contract", diagnostics);

        if (!string.Equals(package.Manifest.PluginId, package.Module.Id, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK011", "Plugin manifest id must match module id."));
        }

        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.PluginId, out var metadataPluginId) ||
            !string.Equals(metadataPluginId, package.Manifest.PluginId, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK012", "Plugin module metadata must bind to the manifest plugin id."));
        }

        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.Kernel, out var metadataKernel) ||
            string.IsNullOrWhiteSpace(metadataKernel))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK013", "Plugin module metadata must bind to the manifest kernel."));
        }
        else
        {
            PluginManifestTextValidator.ValidateText(metadataKernel, "kernel metadata", diagnostics);
        }

        var rpcEntrypoint = package.Manifest.RpcEntrypoint;
        if (string.IsNullOrWhiteSpace(rpcEntrypoint))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK070", "Server extension manifest must declare an rpcEntrypoint."));
        }
        else
        {
            if (!PluginEntrypointIndex.Build(package).Contains(rpcEntrypoint))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK071",
                    $"Server extension entrypoint '{rpcEntrypoint}' is missing or not a public entrypoint."));
            }

            ValidateRpcEntrypointAliases(package.Entrypoints, rpcEntrypoint, diagnostics);
        }

        if (package.Manifest.Subscriptions.Count > 0)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK073",
                "Server extension manifests must not declare hook subscriptions."));
        }

        ValidateMode(package.Manifest, diagnostics);
        _ = PluginManifestEffectValidator.Validate(package.Manifest, diagnostics);
        ValidateLiveSettings(package.Manifest, diagnostics);
        ThrowIfErrors(diagnostics);
    }

    private static void ValidateRpcEntrypointAliases(
        KernelEntrypoints entrypoints,
        string rpcEntrypoint,
        List<SandboxDiagnostic> diagnostics)
    {
        if (string.Equals(entrypoints.ShouldHandle, rpcEntrypoint, StringComparison.Ordinal) &&
            string.Equals(entrypoints.Handle, rpcEntrypoint, StringComparison.Ordinal))
        {
            return;
        }

        diagnostics.Add(new SandboxDiagnostic(
            "DBXK074",
            "Server extension ShouldHandle and Handle entrypoint aliases must match rpcEntrypoint."));
    }

    public static void ValidatePrepared(PluginPackage package, ExecutionPlan plan, SandboxPolicy installPolicy)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        var manifestEffects = PluginManifestEffectValidator.Validate(package.Manifest, diagnostics);
        var entrypointId = package.Manifest.RpcEntrypoint;
        if (string.IsNullOrWhiteSpace(entrypointId) ||
            !PluginEntrypointIndex.Build(package).TryGet(entrypointId, out var entrypoint))
        {
            ThrowIfErrors(diagnostics);
            return;
        }

        if (plan.FunctionAnalysis.TryGetValue(entrypointId, out var analysis))
        {
            if (diagnostics.Count == 0 && manifestEffects != analysis.Effects)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK041",
                    $"Plugin manifest effects '{manifestEffects}' do not match verified entrypoint effects '{analysis.Effects}'."));
            }

            if ((analysis.Effects & SandboxEffect.Concurrency) != 0 &&
                !installPolicy.GrantsCapability(RuntimeCapabilityIds.Async))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK043",
                    $"Plugin requires async but policy does not grant '{RuntimeCapabilityIds.Async}'."));
            }

            if (!analysis.ReturnType.IsKnown())
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK072", $"Server extension return type '{analysis.ReturnType}' is not supported."));
            }
        }

        PluginManifestCapabilityValidator.Validate(
            package.Manifest,
            plan,
            [entrypointId],
            diagnostics,
            allowNonBindingCapabilities: false);
        PluginManifestCapabilityValidator.ValidateRequiredCapabilityGrants(
            package.Manifest,
            installPolicy,
            diagnostics);
        ValidateLiveSettingSuffix(package.Manifest.LiveSettings, entrypoint, diagnostics);
        ThrowIfErrors(diagnostics);
    }

    private static void ValidateLiveSettingSuffix(
        IReadOnlyList<LiveSettingDefinition> settings,
        SandboxFunction function,
        List<SandboxDiagnostic> diagnostics)
    {
        if (settings.Count == 0)
        {
            return;
        }

        if (function.Parameters.Count < settings.Count)
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK035", $"Server extension entrypoint '{function.Id}' is missing live setting parameters."));
            return;
        }

        var offset = function.Parameters.Count - settings.Count;
        for (var i = 0; i < settings.Count; i++)
        {
            var parameter = function.Parameters[offset + i];
            var expected = LiveSettingTypeConverter.ToSandboxType(settings[i].Type);
            if (!string.Equals(parameter.Name, settings[i].Name, StringComparison.Ordinal) || parameter.Type != expected)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK035",
                    $"Server extension entrypoint '{function.Id}' must declare live setting '{settings[i].Name}' as a trailing parameter of type '{expected}'."));
            }
        }
    }

    private static void ValidateMode(PluginManifest manifest, List<SandboxDiagnostic> diagnostics)
    {
        if (!Enum.IsDefined(manifest.Mode))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK042", "Plugin manifest execution mode is not supported."));
        }
    }

    private static void ValidateLiveSettings(PluginManifest manifest, List<SandboxDiagnostic> diagnostics)
    {
        foreach (var group in manifest.LiveSettings.GroupBy(s => s.Name, StringComparer.Ordinal))
        {
            if (group.Skip(1).Any())
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK021", $"Live setting '{group.Key}' is declared more than once."));
            }
        }

        foreach (var setting in manifest.LiveSettings)
        {
            PluginManifestTextValidator.ValidateText(setting.Name, "live setting name", diagnostics);
            PluginManifestTextValidator.ValidateText(setting.Type, "live setting type", diagnostics);
            try
            {
                _ = LiveSettingTypeConverter.ToSandboxType(setting.Type);
                _ = LiveSettingTypeConverter.ToSandboxValue(setting.Type, setting.DefaultValue);
                LiveSettingTypeConverter.ValidateRangeDefinition(setting);
            }
            catch (SandboxValidationException ex)
            {
                diagnostics.AddRange(ex.Diagnostics);
            }
            catch (Exception)
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK020", $"Live setting type '{setting.Type}' is not supported."));
            }
        }
    }

    private static void ThrowIfErrors(IReadOnlyList<SandboxDiagnostic> diagnostics)
    {
        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }
    }
}
