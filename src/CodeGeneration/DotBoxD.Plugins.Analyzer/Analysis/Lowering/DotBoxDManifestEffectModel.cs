using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDManifestEffectModel
{
    private static readonly EquatableArray<string> NonAllocatingComputeEffects =
        EquatableArray<string>.FromOwned(new[] {
            DotBoxDGenerationNames.Effects.Cpu
        });

    private static readonly EquatableArray<string> AllocatingComputeEffects =
        EquatableArray<string>.FromOwned(new[] {
            DotBoxDGenerationNames.Effects.Cpu,
            DotBoxDGenerationNames.Effects.Alloc
        });

    private static readonly EquatableArray<string> NonAllocatingEffects =
        EquatableArray<string>.FromOwned(new[] {
            DotBoxDGenerationNames.Effects.Cpu,
            DotBoxDGenerationNames.Effects.HostStateWrite,
            DotBoxDGenerationNames.Effects.Concurrency,
            DotBoxDGenerationNames.Effects.Audit
        });

    private static readonly EquatableArray<string> AllocatingEffects =
        EquatableArray<string>.FromOwned(new[] {
            DotBoxDGenerationNames.Effects.Cpu,
            DotBoxDGenerationNames.Effects.Alloc,
            DotBoxDGenerationNames.Effects.HostStateWrite,
            DotBoxDGenerationNames.Effects.Concurrency,
            DotBoxDGenerationNames.Effects.Audit
        });

    public static EquatableArray<string> Create(
        DotBoxDStatementBodyModel shouldHandle,
        DotBoxDStatementBodyModel handleBody,
        ICollection<string>? extraEffects = null)
    {
        var baseEffects = shouldHandle.Allocates || handleBody.Allocates
            ? AllocatingEffects
            : NonAllocatingEffects;
        return Merge(baseEffects, extraEffects);
    }

    public static EquatableArray<string> CreateLocalCallback(
        DotBoxDStatementBodyModel shouldHandle,
        DotBoxDStatementBodyModel handleBody,
        ICollection<string>? extraEffects = null)
    {
        var baseEffects = shouldHandle.Allocates || handleBody.Allocates
            ? AllocatingComputeEffects
            : NonAllocatingComputeEffects;
        return Merge(baseEffects, extraEffects);
    }

    private static EquatableArray<string> Merge(
        EquatableArray<string> baseEffects,
        ICollection<string>? extraEffects)
    {
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
