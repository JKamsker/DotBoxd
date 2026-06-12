namespace SafeIR.Plugins;

using SafeIR;

internal delegate SandboxEffect ManifestEffectsValidator(
    PluginManifest manifest,
    List<SandboxDiagnostic> diagnostics);

internal static class PluginPreparedPackageValidator
{
    public static void Validate(
        PluginPackage package,
        ExecutionPlan plan,
        PluginEventAdapterRegistry events,
        ManifestEffectsValidator validateManifestEffects)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        var manifestEffects = validateManifestEffects(package.Manifest, diagnostics);
        var planEffects = EntrypointEffects(package, plan);
        if (diagnostics.Count == 0 && manifestEffects != planEffects)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "SGP041",
                $"Plugin manifest effects '{manifestEffects}' do not match verified entrypoint effects '{planEffects}'."));
        }

        var contractEvent = ValidateContract(package, diagnostics);
        ValidatePreparedEntrypoints(package, plan, events, contractEvent, diagnostics);
        ThrowIfErrors(diagnostics);
    }

    private static SandboxEffect EntrypointEffects(PluginPackage package, ExecutionPlan plan)
    {
        var effects = SandboxEffect.None;
        if (plan.FunctionAnalysis.TryGetValue(package.Entrypoints.ShouldHandle, out var shouldHandle))
        {
            effects |= shouldHandle.Effects;
        }

        if (plan.FunctionAnalysis.TryGetValue(package.Entrypoints.Handle, out var handle))
        {
            effects |= handle.Effects;
        }

        return effects;
    }

    private static string? ValidateContract(PluginPackage package, List<SandboxDiagnostic> diagnostics)
    {
        if (!package.Manifest.Contract.StartsWith(PluginManifestNames.EventKernelContract.Prefix, StringComparison.Ordinal) ||
            !package.Manifest.Contract.EndsWith(PluginManifestNames.EventKernelContract.Suffix, StringComparison.Ordinal) ||
            package.Manifest.Contract.Length <= PluginManifestNames.EventKernelContract.Empty.Length)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "SGP014",
                "Plugin manifest contract must be an IEventKernel<TEvent> contract."));
            return null;
        }

        var eventName = package.Manifest.Contract[
            PluginManifestNames.EventKernelContract.Prefix.Length..^PluginManifestNames.EventKernelContract.SuffixLength];
        foreach (var subscription in package.Manifest.Subscriptions)
        {
            if (!string.Equals(subscription.Event, eventName, StringComparison.Ordinal))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "SGP014",
                    $"Plugin manifest contract event '{eventName}' must match subscription event '{subscription.Event}'."));
            }
        }

        return eventName;
    }

    private static void ValidatePreparedEntrypoints(
        PluginPackage package,
        ExecutionPlan plan,
        PluginEventAdapterRegistry events,
        string? contractEvent,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!TryGetEntrypoint(package, package.Entrypoints.ShouldHandle, out var shouldHandle) ||
            !TryGetEntrypoint(package, package.Entrypoints.Handle, out var handle))
        {
            return;
        }

        ValidateReturnTypes(plan, shouldHandle, handle, diagnostics);
        if (!ParametersMatch(shouldHandle.Parameters, handle.Parameters))
        {
            diagnostics.Add(new SandboxDiagnostic("SGP034", "Kernel entrypoints must use the same parameter shape."));
        }

        ValidateLiveSettingSuffix(package.Manifest.LiveSettings, shouldHandle, diagnostics);
        ValidateLiveSettingSuffix(package.Manifest.LiveSettings, handle, diagnostics);

        var expected = ExpectedParameters(package.Manifest, events, contractEvent);
        if (expected is not null)
        {
            ValidateExactParameters(shouldHandle, expected, diagnostics);
            ValidateExactParameters(handle, expected, diagnostics);
        }
    }

    private static void ValidateReturnTypes(
        ExecutionPlan plan,
        SandboxFunction shouldHandle,
        SandboxFunction handle,
        List<SandboxDiagnostic> diagnostics)
    {
        if (plan.FunctionAnalysis.TryGetValue(shouldHandle.Id, out var shouldAnalysis) &&
            shouldAnalysis.ReturnType != SandboxType.Bool)
        {
            diagnostics.Add(new SandboxDiagnostic("SGP033", "Kernel ShouldHandle entrypoint must return Bool."));
        }

        if (plan.FunctionAnalysis.TryGetValue(handle.Id, out var handleAnalysis) &&
            handleAnalysis.ReturnType != SandboxType.Unit)
        {
            diagnostics.Add(new SandboxDiagnostic("SGP033", "Kernel Handle entrypoint must return Unit."));
        }
    }

    private static IReadOnlyList<Parameter>? ExpectedParameters(
        PluginManifest manifest,
        PluginEventAdapterRegistry events,
        string? contractEvent)
    {
        var eventName = manifest.Subscriptions.FirstOrDefault()?.Event ?? contractEvent;
        if (string.IsNullOrWhiteSpace(eventName) || !events.TryResolveShape(eventName, out var shape))
        {
            return null;
        }

        return shape.Parameters
            .Concat(manifest.LiveSettings.Select(s => new Parameter(s.Name, LiveSettingTypeConverter.ToSandboxType(s.Type))))
            .ToArray();
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
            diagnostics.Add(new SandboxDiagnostic("SGP035", $"Kernel entrypoint '{function.Id}' is missing live setting parameters."));
            return;
        }

        var offset = function.Parameters.Count - settings.Count;
        for (var i = 0; i < settings.Count; i++)
        {
            var setting = settings[i];
            var parameter = function.Parameters[offset + i];
            var expected = LiveSettingTypeConverter.ToSandboxType(setting.Type);
            if (!string.Equals(parameter.Name, setting.Name, StringComparison.Ordinal) ||
                parameter.Type != expected)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "SGP035",
                    $"Kernel entrypoint '{function.Id}' must declare live setting '{setting.Name}' as trailing parameter with type '{expected}'."));
            }
        }
    }

    private static bool TryGetEntrypoint(PluginPackage package, string functionId, out SandboxFunction function)
    {
        function = package.Module.Functions.FirstOrDefault(f =>
            f.IsEntrypoint && string.Equals(f.Id, functionId, StringComparison.Ordinal))!;
        return function is not null;
    }

    private static bool ParametersMatch(IReadOnlyList<Parameter> first, IReadOnlyList<Parameter> second)
        => first.Count == second.Count &&
           first.Zip(second).All(pair =>
               string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
               pair.First.Type == pair.Second.Type);

    private static void ValidateExactParameters(
        SandboxFunction function,
        IReadOnlyList<Parameter> expected,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!ParametersMatch(function.Parameters, expected))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "SGP033",
                $"Kernel entrypoint '{function.Id}' must declare event adapter parameters first, followed by live settings, with exact names and types."));
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
