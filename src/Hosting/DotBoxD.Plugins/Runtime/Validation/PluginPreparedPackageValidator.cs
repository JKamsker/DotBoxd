using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Lifecycle;
using DotBoxD.Plugins.Runtime.Validation;

namespace DotBoxD.Plugins.Runtime;

internal delegate SandboxEffect ManifestEffectsValidator(
    PluginManifest manifest,
    List<SandboxDiagnostic> diagnostics);

internal static partial class PluginPreparedPackageValidator
{
    public static void Validate(
        PluginPackage package,
        ExecutionPlan plan,
        PluginEventAdapterRegistry events,
        SandboxPolicy installPolicy,
        ManifestEffectsValidator validateManifestEffects)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        var manifestEffects = validateManifestEffects(package.Manifest, diagnostics);
        var planEffects = EntrypointEffects(package, plan);
        if (diagnostics.Count == 0 && manifestEffects != planEffects)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK041",
                $"Plugin manifest effects '{manifestEffects}' do not match verified entrypoint effects '{planEffects}'."));
        }

        ValidateAsyncCapability(package, plan, diagnostics);
        PluginManifestCapabilityValidator.Validate(
            package.Manifest,
            plan,
            [package.Entrypoints.ShouldHandle, package.Entrypoints.Handle],
            diagnostics);
        PluginManifestCapabilityValidator.ValidateRequiredCapabilityGrants(
            package.Manifest,
            installPolicy,
            diagnostics);
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

    private static void ValidateAsyncCapability(
        PluginPackage package,
        ExecutionPlan plan,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!EntrypointRequiresAsync(package.Entrypoints.ShouldHandle, plan) &&
            !EntrypointRequiresAsync(package.Entrypoints.Handle, plan))
        {
            return;
        }

        if (plan.Policy.GrantsCapability(RuntimeCapabilityIds.Async))
        {
            return;
        }

        diagnostics.Add(new SandboxDiagnostic(
            "DBXK043",
            $"Plugin requires async but policy does not grant '{RuntimeCapabilityIds.Async}'."));
    }

    private static bool EntrypointRequiresAsync(string entrypoint, ExecutionPlan plan)
        => plan.FunctionAnalysis.TryGetValue(entrypoint, out var analysis) &&
           (analysis.Effects & SandboxEffect.Concurrency) != 0;

    private static string? ValidateContract(PluginPackage package, List<SandboxDiagnostic> diagnostics)
    {
        if (!package.Manifest.Contract.StartsWith(PluginManifestNames.EventKernelContract.Prefix, StringComparison.Ordinal) ||
            !package.Manifest.Contract.EndsWith(PluginManifestNames.EventKernelContract.Suffix, StringComparison.Ordinal) ||
            package.Manifest.Contract.Length <= PluginManifestNames.EventKernelContract.Empty.Length)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK014",
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
                    "DBXK014",
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
        var entrypointIndex = PluginEntrypointIndex.Build(package);
        if (!entrypointIndex.TryGet(package.Entrypoints.ShouldHandle, out var shouldHandle) ||
            !entrypointIndex.TryGet(package.Entrypoints.Handle, out var handle))
        {
            return;
        }

        // RunLocal and sandbox result Register return a non-Unit Handle. Ordinary chains and result
        // RegisterLocal keep a Unit-returning Handle.
        var handleReturnsValue = false;
        foreach (var subscription in package.Manifest.Subscriptions)
        {
            if ((subscription.LocalTerminal && subscription.ProjectedType is not null) ||
                (subscription.ResultType is not null && !subscription.ResultLocalTerminal))
            {
                handleReturnsValue = true;
                break;
            }
        }

        ValidateReturnTypes(plan, shouldHandle, handle, handleReturnsValue, diagnostics);
        ValidateLocalTerminalRouting(package, plan, handle.Id, diagnostics);
        if (!ParametersMatch(shouldHandle.Parameters, handle.Parameters))
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK034", "Kernel entrypoints must use the same parameter shape."));
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
        bool handleReturnsValue,
        List<SandboxDiagnostic> diagnostics)
    {
        if (plan.FunctionAnalysis.TryGetValue(shouldHandle.Id, out var shouldAnalysis) &&
            shouldAnalysis.ReturnType != SandboxType.Bool)
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK033", "Kernel ShouldHandle entrypoint must return Bool."));
        }

        if (!plan.FunctionAnalysis.TryGetValue(handle.Id, out var handleAnalysis))
        {
            return;
        }

        // A projection RunLocal chain or sandbox result Register returns a value. The exact projection/result
        // type is enforced by the downstream decoder.
        if (handleReturnsValue)
        {
            if (handleAnalysis.ReturnType == SandboxType.Unit)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK033",
                    "Kernel Handle entrypoint must return the projected or result value, not Unit."));
            }
        }
        else if (handleAnalysis.ReturnType != SandboxType.Unit)
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK033", "Kernel Handle entrypoint must return Unit."));
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

        return PluginParameterShape.BuildExpected(shape.Parameters, manifest.LiveSettings);
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
            diagnostics.Add(new SandboxDiagnostic("DBXK035", $"Kernel entrypoint '{function.Id}' is missing live setting parameters."));
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
                    "DBXK035",
                    $"Kernel entrypoint '{function.Id}' must declare live setting '{setting.Name}' as trailing parameter with type '{expected}'."));
            }
        }
    }

    private static bool ParametersMatch(IReadOnlyList<Parameter> first, IReadOnlyList<Parameter> second)
        => PluginParameterShape.Matches(first, second);

    private static void ValidateExactParameters(
        SandboxFunction function,
        IReadOnlyList<Parameter> expected,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!ParametersMatch(function.Parameters, expected))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK033",
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
