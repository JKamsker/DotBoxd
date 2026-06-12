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
        if (!manifest.Subscriptions.Any(s => string.Equals(s.Event, adapter.EventName, StringComparison.Ordinal)))
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP031", $"Plugin '{manifest.PluginId}' is not subscribed to event '{adapter.EventName}'.")
            ]);
        }

        var expected = adapter.Parameters
            .Concat(manifest.LiveSettings.Select(s => new Parameter(s.Name, LiveSettingTypeConverter.ToSandboxType(s.Type))))
            .ToArray();
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

        if (function.ReturnType != returnType || function.Parameters.Count != expected.Count)
        {
            throw SignatureError(functionId);
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (function.Parameters[i].Type != expected[i].Type)
            {
                throw SignatureError(functionId);
            }
        }
    }

    private static SandboxValidationException SignatureError(string functionId)
        => new([new SandboxDiagnostic("SGP033", $"Kernel entrypoint '{functionId}' does not match the hook event and live settings.")]);
}
