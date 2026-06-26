using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Lifecycle;
using DotBoxD.Plugins.Runtime.Validation;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginPackageValidator
{
    public static void Validate(PluginPackage package)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        if (string.IsNullOrWhiteSpace(package.Manifest.PluginId))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK010", "Plugin id is required."));
        }

        ValidateManifestText(package.Manifest.PluginId, "plugin id", diagnostics);
        ValidateManifestText(package.Manifest.Contract, "plugin contract", diagnostics);

        if (!string.Equals(package.Manifest.PluginId, package.Module.Id, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK011", "Plugin manifest id must match module id."));
        }

        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.PluginId, out var metadataPluginId) ||
            !string.Equals(metadataPluginId, package.Manifest.PluginId, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK012", "Plugin module metadata must bind to the manifest plugin id."));
        }

        var metadataKernel = ValidateModuleKernelMetadata(package, diagnostics);
        ValidateManifestMode(package.Manifest, diagnostics);
        PluginManifestEffectValidator.Validate(package.Manifest, diagnostics);
        ValidateEntrypoints(package, PluginEntrypointIndex.Build(package), diagnostics);
        foreach (var group in package.Manifest.LiveSettings.GroupBy(s => s.Name, StringComparer.Ordinal))
        {
            if (group.Skip(1).Any())
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK021", $"Live setting '{group.Key}' is declared more than once."));
            }
        }

        foreach (var setting in package.Manifest.LiveSettings)
        {
            ValidateManifestText(setting.Name, "live setting name", diagnostics);
            ValidateManifestText(setting.Type, "live setting type", diagnostics);
            ValidateSetting(setting, diagnostics);
        }

        if (package.Manifest.Subscriptions.Count == 0)
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK030", "At least one hook subscription is required."));
        }
        else if (package.Manifest.Subscriptions.Count > 1)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK031",
                "A plugin package must declare exactly one hook subscription."));
        }

        foreach (var subscription in package.Manifest.Subscriptions)
        {
            if (string.IsNullOrWhiteSpace(subscription.Event) || string.IsNullOrWhiteSpace(subscription.Kernel))
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK031", "Hook subscription event and kernel are required."));
            }

            ValidateManifestText(subscription.Event, "hook subscription event", diagnostics);
            ValidateManifestText(subscription.Kernel, "hook subscription kernel", diagnostics);
            ValidateResultMetadata(subscription, diagnostics);
            if (!string.IsNullOrWhiteSpace(metadataKernel) &&
                !string.Equals(subscription.Kernel, metadataKernel, StringComparison.Ordinal))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK013",
                    $"Hook subscription kernel '{subscription.Kernel}' must match module kernel '{metadataKernel}'."));
            }

            ValidateIndexedPredicates(subscription, diagnostics);
        }

        ThrowIfErrors(diagnostics);
    }

    public static void ValidatePrepared(
        PluginPackage package,
        ExecutionPlan plan,
        PluginEventAdapterRegistry events,
        SandboxPolicy installPolicy)
        => PluginPreparedPackageValidator.Validate(package, plan, events, installPolicy, PluginManifestEffectValidator.Validate);

    private static string? ValidateModuleKernelMetadata(PluginPackage package, List<SandboxDiagnostic> diagnostics)
    {
        if (!package.Module.Metadata.TryGetValue(PluginManifestNames.ModuleMetadata.Kernel, out var metadataKernel) ||
            string.IsNullOrWhiteSpace(metadataKernel))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK013", "Plugin module metadata must bind to the manifest kernel."));
            return null;
        }

        ValidateManifestText(metadataKernel, "kernel metadata", diagnostics);
        return metadataKernel;
    }

    private static void ValidateIndexedPredicates(
        HookSubscriptionManifest subscription,
        List<SandboxDiagnostic> diagnostics)
    {
        foreach (var predicate in subscription.IndexedPredicates)
        {
            ValidateManifestText(predicate.Path, "indexed predicate path", diagnostics);
            if (!Enum.IsDefined(predicate.Operator))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK046",
                    $"Indexed predicate operator '{predicate.Operator}' is not supported."));
            }

            if (predicate.ValueType is not ("bool" or "int" or "long" or "double" or "string"))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK047",
                    $"Indexed predicate value type '{predicate.ValueType}' is not supported."));
            }
            else if (!ValueMatchesType(predicate.Value, predicate.ValueType))
            {
                // Defense-in-depth for programmatically-built manifests: the JSON importer already parses
                // the value per valueType, but an in-memory package could box a mismatched runtime type.
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK049",
                    $"Indexed predicate value '{predicate.Value ?? "null"}' does not match its declared value type '{predicate.ValueType}'."));
            }
        }

        if (subscription.IndexCoversPredicate && subscription.IndexedPredicates.Count == 0)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK048",
                "A hook subscription cannot claim full index coverage with no indexed predicates."));
        }
    }

    private static void ValidateResultMetadata(
        HookSubscriptionManifest subscription,
        List<SandboxDiagnostic> diagnostics)
    {
        if (subscription.ResultType is null)
        {
            if (subscription.ResultLocalTerminal)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "A result-local hook subscription must declare resultType."));
            }

            return;
        }

        ValidateManifestText(subscription.ResultType, "hook result type", diagnostics);
        if (subscription.LocalTerminal || subscription.ProjectedType is not null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK031",
                "A hook subscription cannot combine result hook metadata with RunLocal projection metadata."));
        }
    }

    private static bool ValueMatchesType(object? value, string valueType)
        => valueType switch
        {
            "bool" => value is bool,
            "int" => value is int,
            "long" => value is long,
            "double" => value is double,
            "string" => value is string,
            _ => false
        };

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
            diagnostics.Add(new SandboxDiagnostic("DBXK042", "Plugin manifest execution mode is not supported."));
        }
    }

    private static void ValidateEntrypoint(
        PluginEntrypointIndex entrypointIndex,
        string functionId,
        string name,
        List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(functionId))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK032", $"Kernel {name} entrypoint is required."));
            return;
        }

        ValidateManifestText(functionId, $"kernel {name} entrypoint", diagnostics);

        if (!entrypointIndex.Contains(functionId))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK032", $"Kernel entrypoint '{functionId}' is missing or not public."));
        }
    }

    private static void ValidateManifestText(string value, string description, List<SandboxDiagnostic> diagnostics)
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

    private static void ValidateSetting(LiveSettingDefinition setting, List<SandboxDiagnostic> diagnostics)
    {
        try
        {
            _ = LiveSettingTypeConverter.ToSandboxType(setting.Type);
            _ = LiveSettingTypeConverter.ToSandboxValue(setting.Type, setting.DefaultValue);
            ValidateRange(setting, diagnostics);
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

    private static void ValidateRange(LiveSettingDefinition setting, List<SandboxDiagnostic> diagnostics)
    {
        try
        {
            LiveSettingTypeConverter.ValidateRangeDefinition(setting);
        }
        catch (SandboxValidationException ex)
        {
            diagnostics.AddRange(ex.Diagnostics);
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
