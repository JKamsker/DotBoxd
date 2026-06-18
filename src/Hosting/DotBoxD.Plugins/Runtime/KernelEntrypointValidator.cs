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
        => Validate(manifest, plan, entrypoints, adapterShape, requireUnitHandle: true);

    public static void ValidateLocalCallback(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        PluginEventShape adapterShape)
        => Validate(manifest, plan, entrypoints, adapterShape, requireUnitHandle: false);

    private static void Validate(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        PluginEventShape adapterShape,
        bool requireUnitHandle)
    {
        if (!manifest.Subscriptions.Any(s => EventNameMatch.Matches(s.Event, adapterShape.EventName)))
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK031", $"Plugin '{manifest.PluginId}' is not subscribed to event '{adapterShape.EventName}'.")
            ]);
        }

        var expected = PluginParameterShape.BuildExpected(adapterShape.Parameters, manifest.LiveSettings);
        ValidateFunction(plan, entrypoints.ShouldHandle, SandboxType.Bool, expected);
        ValidateFunction(plan, entrypoints.Handle, requireUnitHandle ? SandboxType.Unit : null, expected);
    }

    private static void ValidateFunction(
        ExecutionPlan plan,
        string functionId,
        SandboxType? returnType,
        IReadOnlyList<Parameter> expected)
    {
        var function = plan.Module.Functions.FirstOrDefault(f => string.Equals(f.Id, functionId, StringComparison.Ordinal));
        if (function is null || !function.IsEntrypoint)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK032", $"Kernel entrypoint '{functionId}' is missing or not public.")
            ]);
        }

        if ((returnType is not null && function.ReturnType != returnType) ||
            !PluginParameterShape.Matches(function.Parameters, expected))
        {
            throw SignatureError(functionId);
        }
    }

    private static SandboxValidationException SignatureError(string functionId)
        => new([new SandboxDiagnostic("DBXK033", $"Kernel entrypoint '{functionId}' does not match the hook event and live settings.")]);
}
