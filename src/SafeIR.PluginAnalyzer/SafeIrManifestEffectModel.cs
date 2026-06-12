namespace SafeIR.PluginAnalyzer;

internal static class SafeIrManifestEffectModel
{
    public static EquatableArray<string> Create(
        SafeIrExpressionModel shouldHandle,
        SafeIrHandleModel handle)
    {
        var effects = new List<string> { "Cpu" };
        if (shouldHandle.Allocates || handle.Allocates) {
            effects.Add("Alloc");
        }

        effects.Add("GameStateWrite");
        effects.Add("Audit");
        return new EquatableArray<string>(effects);
    }
}
