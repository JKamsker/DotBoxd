namespace SafeIR.PluginAnalyzer;

internal static class SafeIrManifestEffectModel
{
    public static EquatableArray<string> Create(
        SafeIrStatementBodyModel shouldHandle,
        SafeIrHandleModel handle)
    {
        var effects = new List<string> { SafeIrGenerationNames.Effects.Cpu };
        if (shouldHandle.Allocates || handle.Allocates) {
            effects.Add(SafeIrGenerationNames.Effects.Alloc);
        }

        effects.Add(SafeIrGenerationNames.Effects.GameStateWrite);
        effects.Add(SafeIrGenerationNames.Effects.Audit);
        return new EquatableArray<string>(effects);
    }
}
