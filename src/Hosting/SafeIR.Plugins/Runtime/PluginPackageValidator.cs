namespace SafeIR.Plugins;

using SafeIR;

internal static class PluginPackageValidator
{
    public static void Validate(PluginPackage package)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        if (string.IsNullOrWhiteSpace(package.Manifest.PluginId)) {
            diagnostics.Add(new SandboxDiagnostic("SGP010", "Plugin id is required."));
        }

        ValidateManifestText(package.Manifest.PluginId, "plugin id", diagnostics);
        ValidateManifestText(package.Manifest.Contract, "plugin contract", diagnostics);

        if (!string.Equals(package.Manifest.PluginId, package.Module.Id, StringComparison.Ordinal)) {
            diagnostics.Add(new SandboxDiagnostic("SGP011", "Plugin manifest id must match module id."));
        }

        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.PluginId, out var metadataPluginId) ||
            !string.Equals(metadataPluginId, package.Manifest.PluginId, StringComparison.Ordinal)) {
            diagnostics.Add(new SandboxDiagnostic("SGP012", "Plugin module metadata must bind to the manifest plugin id."));
        }

        var metadataKernel = ValidateModuleKernelMetadata(package, diagnostics);
        ValidateManifestMode(package.Manifest, diagnostics);
        ValidateManifestEffects(package.Manifest, diagnostics);
        ValidateEntrypoints(package, PluginEntrypointIndex.Build(package), diagnostics);
        foreach (var group in package.Manifest.LiveSettings.GroupBy(s => s.Name, StringComparer.Ordinal)) {
            if (group.Skip(1).Any()) {
                diagnostics.Add(new SandboxDiagnostic("SGP021", $"Live setting '{group.Key}' is declared more than once."));
            }
        }

        foreach (var setting in package.Manifest.LiveSettings) {
            ValidateManifestText(setting.Name, "live setting name", diagnostics);
            ValidateManifestText(setting.Type, "live setting type", diagnostics);
            ValidateSetting(setting, diagnostics);
        }

        if (package.Manifest.Subscriptions.Count == 0) {
            diagnostics.Add(new SandboxDiagnostic("SGP030", "At least one hook subscription is required."));
        }

        foreach (var subscription in package.Manifest.Subscriptions) {
            if (string.IsNullOrWhiteSpace(subscription.Event) || string.IsNullOrWhiteSpace(subscription.Kernel)) {
                diagnostics.Add(new SandboxDiagnostic("SGP031", "Hook subscription event and kernel are required."));
            }

            ValidateManifestText(subscription.Event, "hook subscription event", diagnostics);
            ValidateManifestText(subscription.Kernel, "hook subscription kernel", diagnostics);
            if (!string.IsNullOrWhiteSpace(metadataKernel) &&
                !string.Equals(subscription.Kernel, metadataKernel, StringComparison.Ordinal)) {
                diagnostics.Add(new SandboxDiagnostic(
                    "SGP013",
                    $"Hook subscription kernel '{subscription.Kernel}' must match module kernel '{metadataKernel}'."));
            }
        }

        ThrowIfErrors(diagnostics);
    }

    public static void ValidatePrepared(
        PluginPackage package,
        ExecutionPlan plan,
        PluginEventAdapterRegistry events)
        => PluginPreparedPackageValidator.Validate(package, plan, events, ValidateManifestEffects);

    private static string? ValidateModuleKernelMetadata(PluginPackage package, List<SandboxDiagnostic> diagnostics)
    {
        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.Kernel, out var metadataKernel) ||
            string.IsNullOrWhiteSpace(metadataKernel)) {
            diagnostics.Add(new SandboxDiagnostic("SGP013", "Plugin module metadata must bind to the manifest kernel."));
            return null;
        }

        ValidateManifestText(metadataKernel, "kernel metadata", diagnostics);
        return metadataKernel;
    }

    private static void ValidateEntrypoints(
        PluginPackage package,
        PluginEntrypointIndex entrypointIndex,
        List<SandboxDiagnostic> diagnostics)
    {
        ValidateEntrypoint(
            entrypointIndex,
            package.Entrypoints.ShouldHandle,
            PluginManifestNames.Entrypoints.ShouldHandle,
            diagnostics);
        ValidateEntrypoint(
            entrypointIndex,
            package.Entrypoints.Handle,
            PluginManifestNames.Entrypoints.Handle,
            diagnostics);
    }

    private static void ValidateManifestMode(PluginManifest manifest, List<SandboxDiagnostic> diagnostics)
    {
        if (!Enum.IsDefined(manifest.Mode))
        {
            diagnostics.Add(new SandboxDiagnostic("SGP042", "Plugin manifest execution mode is not supported."));
        }
    }

    private static void ValidateEntrypoint(
        PluginEntrypointIndex entrypointIndex,
        string functionId,
        string name,
        List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(functionId)) {
            diagnostics.Add(new SandboxDiagnostic("SGP032", $"Kernel {name} entrypoint is required."));
            return;
        }

        ValidateManifestText(functionId, $"kernel {name} entrypoint", diagnostics);

        if (!entrypointIndex.Contains(functionId)) {
            diagnostics.Add(new SandboxDiagnostic("SGP032", $"Kernel entrypoint '{functionId}' is missing or not public."));
        }
    }

    private static void ValidateManifestText(string value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) {
            diagnostics.Add(new SandboxDiagnostic("SGP050", $"Plugin manifest {description} must be non-empty and must not contain control characters."));
            return;
        }

        if (SandboxDescriptorGuards.ContainsForbiddenDescriptor(value)) {
            diagnostics.Add(new SandboxDiagnostic("SGP050", $"Plugin manifest {description} looks like a forbidden CLR or IL descriptor."));
        }
    }

    private static SandboxEffect ValidateManifestEffects(
        PluginManifest manifest,
        List<SandboxDiagnostic> diagnostics)
    {
        var effects = SandboxEffect.None;
        foreach (var effect in manifest.Effects) {
            if (!Enum.TryParse<SandboxEffect>(effect, ignoreCase: false, out var parsed) ||
                parsed == SandboxEffect.None ||
                !parsed.ContainsOnlyKnownBits()) {
                diagnostics.Add(new SandboxDiagnostic("SGP040", $"Plugin manifest effect '{effect}' is not supported."));
                continue;
            }

            effects |= parsed;
        }

        if (effects == SandboxEffect.None) {
            diagnostics.Add(new SandboxDiagnostic("SGP040", "Plugin manifest must declare verified effects."));
        }

        return effects;
    }

    private static void ValidateSetting(LiveSettingDefinition setting, List<SandboxDiagnostic> diagnostics)
    {
        try {
            _ = LiveSettingTypeConverter.ToSandboxType(setting.Type);
            _ = LiveSettingTypeConverter.ToSandboxValue(setting.Type, setting.DefaultValue);
            ValidateRange(setting, diagnostics);
        }
        catch (SandboxValidationException ex) {
            diagnostics.AddRange(ex.Diagnostics);
        }
        catch (Exception) {
            diagnostics.Add(new SandboxDiagnostic("SGP020", $"Live setting type '{setting.Type}' is not supported."));
        }
    }

    private static void ValidateRange(LiveSettingDefinition setting, List<SandboxDiagnostic> diagnostics)
    {
        try {
            LiveSettingTypeConverter.ValidateRangeDefinition(setting);
        }
        catch (SandboxValidationException ex) {
            diagnostics.AddRange(ex.Diagnostics);
        }
    }

    private static void ThrowIfErrors(IReadOnlyList<SandboxDiagnostic> diagnostics)
    {
        if (diagnostics.Count > 0) {
            throw new SandboxValidationException(diagnostics);
        }
    }
}
