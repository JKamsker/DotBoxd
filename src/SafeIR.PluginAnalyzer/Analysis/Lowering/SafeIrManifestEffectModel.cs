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
        SafeIrHandleModel handle,
        ICollection<string>? extraEffects = null)
    {
        var baseEffects = shouldHandle.Allocates || handle.Allocates
            ? AllocatingEffects
            : NonAllocatingEffects;
        if (extraEffects is null || extraEffects.Count == 0)
        {
            return baseEffects;
        }

        // Preserve the base order, then append the host-binding effects (deterministically ordered) the
        // base does not already declare — so a HostStateRead binding adds "HostStateRead" to the manifest.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(baseEffects.Count + extraEffects.Count);
        foreach (var effect in baseEffects)
        {
            if (seen.Add(effect))
            {
                result.Add(effect);
            }
        }

        foreach (var effect in extraEffects)
        {
            if (seen.Add(effect))
            {
                result.Add(effect);
            }
        }

        return EquatableArray<string>.FromOwned([.. result]);
    }
}
