namespace SafeIR.Plugins;

using SafeIR;

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
        if (!manifest.Subscriptions.Any(s => string.Equals(s.Event, adapterShape.EventName, StringComparison.Ordinal)))
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP031", $"Plugin '{manifest.PluginId}' is not subscribed to event '{adapterShape.EventName}'.")
            ]);
        }

        var expected = PluginParameterShape.BuildExpected(adapterShape.Parameters, manifest.LiveSettings);
        ValidateFunction(plan, entrypoints.ShouldHandle, SandboxType.Bool, expected);
        ValidateFunction(plan, entrypoints.Handle, SandboxType.Unit, expected);
    }

    private static void ValidateFunction(
        ExecutionPlan plan,
        string functionId,
        SandboxType returnType,
        IReadOnlyList<Parameter> expected)
    {
        var function = plan.Module.Functions.FirstOrDefault(f => string.Equals(f.Id, functionId, StringComparison.Ordinal));
        if (function is null || !function.IsEntrypoint)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP032", $"Kernel entrypoint '{functionId}' is missing or not public.")
            ]);
        }

        if (function.ReturnType != returnType ||
            !PluginParameterShape.Matches(function.Parameters, expected))
        {
            throw SignatureError(functionId);
        }
    }

    private static SandboxValidationException SignatureError(string functionId)
        => new([new SandboxDiagnostic("SGP033", $"Kernel entrypoint '{functionId}' does not match the hook event and live settings.")]);
}
