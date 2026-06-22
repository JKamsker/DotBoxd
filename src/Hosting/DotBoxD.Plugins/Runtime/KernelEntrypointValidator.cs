using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Plugins.Runtime;

internal static class KernelEntrypointValidator
{
    public static void Validate<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IPluginEventAdapter<TEvent> adapter)
    {
        Validate(manifest, plan, entrypoints, PluginEventShape.From(adapter));
    }

    public static void Validate(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        PluginEventShape adapterShape)
    {
        if (!manifest.Subscriptions.Any(s => EventNameMatch.Matches(s.Event, adapterShape.EventName)))
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK031", $"Plugin '{manifest.PluginId}' is not subscribed to event '{adapterShape.EventName}'.")
            ]);
        }

        var expected = PluginParameterShape.BuildExpected(adapterShape.Parameters, manifest.LiveSettings);
        ValidateFunction(plan, entrypoints.ShouldHandle, SandboxType.Bool, requireNonUnit: false, expected);

        // Handle return contract by chain shape:
        //  - ordinary chain, whole-event RunLocal, and result RegisterLocal return Unit.
        //  - projection RunLocal and sandbox result Register return a non-Unit value.
        //    Projection exact type is enforced by the push decoder; result exact type is enforced when FireAsync
        //    decodes to the hook's declared TResult.
        var handleReturnsValue = ReturnsValue(manifest);
        ValidateFunction(
            plan,
            entrypoints.Handle,
            handleReturnsValue ? null : SandboxType.Unit,
            requireNonUnit: handleReturnsValue,
            expected);
    }

    private static bool ReturnsValue(PluginManifest manifest)
    {
        foreach (var subscription in manifest.Subscriptions)
        {
            if ((subscription.LocalTerminal && subscription.ProjectedType is not null) ||
                (subscription.ResultType is not null && !subscription.ResultLocalTerminal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateFunction(
        ExecutionPlan plan,
        string functionId,
        SandboxType? exactReturnType,
        bool requireNonUnit,
        IReadOnlyList<Parameter> expected)
    {
        var function = plan.Module.Functions.FirstOrDefault(f => string.Equals(f.Id, functionId, StringComparison.Ordinal));
        if (function is null || !function.IsEntrypoint)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK032", $"Kernel entrypoint '{functionId}' is missing or not public.")
            ]);
        }

        var returnTypeOk = (exactReturnType is null || function.ReturnType == exactReturnType)
            && (!requireNonUnit || function.ReturnType != SandboxType.Unit);
        if (!returnTypeOk || !PluginParameterShape.Matches(function.Parameters, expected))
        {
            throw SignatureError(functionId);
        }
    }

    private static SandboxValidationException SignatureError(string functionId)
        => new([new SandboxDiagnostic("DBXK033", $"Kernel entrypoint '{functionId}' does not match the hook event and live settings.")]);
}
