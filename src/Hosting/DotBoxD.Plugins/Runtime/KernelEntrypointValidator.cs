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
        //  - ordinary chain (performs a host send) and whole-event RunLocal (no Select): Handle returns Unit.
        //  - projection RunLocal (with Select): the Handle RETURNS the projected value, so it must be non-Unit.
        //    The projected type may be non-scalar (record/list/enum) and is not round-trippable through the
        //    scalar converter; the exact type is enforced end-to-end when the plugin decodes the pushed value,
        //    so here we require only non-Unit.
        var handleReturnsProjection = ReturnsProjectedValue(manifest);
        ValidateFunction(
            plan,
            entrypoints.Handle,
            handleReturnsProjection ? null : SandboxType.Unit,
            requireNonUnit: handleReturnsProjection,
            expected);
    }

    // True only for a projection RunLocal chain (LocalTerminal with a declared ProjectedType). A whole-event
    // RunLocal (LocalTerminal, no ProjectedType) and an ordinary chain both keep the Unit-returning Handle.
    private static bool ReturnsProjectedValue(PluginManifest manifest)
    {
        foreach (var subscription in manifest.Subscriptions)
        {
            if (subscription.LocalTerminal && subscription.ProjectedType is not null)
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
