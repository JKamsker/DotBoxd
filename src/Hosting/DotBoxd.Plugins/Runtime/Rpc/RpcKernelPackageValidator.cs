namespace DotBoxd.Plugins;

using DotBoxd.Kernels;

/// <summary>
/// Validates a <b>kernel RPC service</b> package — a kernel invoked request/response with caller
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
        ValidateText(package.Manifest.PluginId, "plugin id", diagnostics);
        ValidateText(package.Manifest.Contract, "plugin contract", diagnostics);

        if (!string.Equals(package.Manifest.PluginId, package.Module.Id, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK011", "Plugin manifest id must match module id."));
        }

        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.PluginId, out var metadataPluginId) ||
            !string.Equals(metadataPluginId, package.Manifest.PluginId, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK012", "Plugin module metadata must bind to the manifest plugin id."));
        }

        if (string.IsNullOrWhiteSpace(package.Manifest.RpcEntrypoint))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK070", "Kernel RPC service manifest must declare an rpcEntrypoint."));
        }
        else if (!PluginEntrypointIndex.Build(package).Contains(package.Manifest.RpcEntrypoint))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK071",
                $"Kernel RPC entrypoint '{package.Manifest.RpcEntrypoint}' is missing or not a public entrypoint."));
        }

        ValidateMode(package.Manifest, diagnostics);
        _ = ValidateEffects(package.Manifest, diagnostics);
        ValidateLiveSettings(package.Manifest, diagnostics);
        ThrowIfErrors(diagnostics);
    }

    public static void ValidatePrepared(PluginPackage package, ExecutionPlan plan)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        var manifestEffects = ValidateEffects(package.Manifest, diagnostics);
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

            if (!analysis.ReturnType.IsKnown() || analysis.ReturnType.IsForbidden())
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK072", $"Kernel RPC return type '{analysis.ReturnType}' is not supported."));
            }
        }

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
            diagnostics.Add(new SandboxDiagnostic("DBXK035", $"Kernel RPC entrypoint '{function.Id}' is missing live setting parameters."));
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
                    $"Kernel RPC entrypoint '{function.Id}' must declare live setting '{settings[i].Name}' as a trailing parameter of type '{expected}'."));
            }
        }
    }

    private static SandboxEffect ValidateEffects(PluginManifest manifest, List<SandboxDiagnostic> diagnostics)
    {
        var effects = SandboxEffect.None;
        foreach (var effect in manifest.Effects)
        {
            if (!Enum.TryParse<SandboxEffect>(effect, ignoreCase: false, out var parsed) ||
                parsed == SandboxEffect.None ||
                !parsed.ContainsOnlyKnownBits())
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK040", $"Plugin manifest effect '{effect}' is not supported."));
                continue;
            }

            effects |= parsed;
        }

        if (effects == SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK040", "Plugin manifest must declare verified effects."));
        }

        return effects;
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
            ValidateText(setting.Name, "live setting name", diagnostics);
            ValidateText(setting.Type, "live setting type", diagnostics);
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

    private static void ValidateText(string value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK050", $"Plugin manifest {description} must be non-empty and must not contain control characters."));
            return;
        }

        if (SandboxDescriptorGuards.ContainsForbiddenDescriptor(value))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK050", $"Plugin manifest {description} looks like a forbidden CLR or IL descriptor."));
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
