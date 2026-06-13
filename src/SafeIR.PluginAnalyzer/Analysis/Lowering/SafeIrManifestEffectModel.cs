namespace SafeIR.PluginAnalyzer;

internal static class SafeIrManifestEffectModel
{
    private static readonly EquatableArray<string> NonAllocatingEffects =
        EquatableArray<string>.FromOwned(new[] {
            SafeIrGenerationNames.Effects.Cpu,
            SafeIrGenerationNames.Effects.HostStateWrite,
            SafeIrGenerationNames.Effects.Audit
        });

    private static readonly EquatableArray<string> AllocatingEffects =
        EquatableArray<string>.FromOwned(new[] {
            SafeIrGenerationNames.Effects.Cpu,
            SafeIrGenerationNames.Effects.Alloc,
            SafeIrGenerationNames.Effects.HostStateWrite,
            SafeIrGenerationNames.Effects.Audit
        });

    public static EquatableArray<string> Create(
        SafeIrStatementBodyModel shouldHandle,
        SafeIrHandleModel handle)
    {
        return shouldHandle.Allocates || handle.Allocates
            ? AllocatingEffects
            : NonAllocatingEffects;
    }
}
