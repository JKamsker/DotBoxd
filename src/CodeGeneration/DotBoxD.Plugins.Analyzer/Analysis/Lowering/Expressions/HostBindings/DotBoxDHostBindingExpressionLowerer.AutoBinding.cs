using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static IReadOnlyList<string> AutoHostBindingSandboxEffects(
        TypedConstant effects,
        bool returnAllocates,
        IMethodSymbol method)
    {
        if (effects.Value is null)
        {
            throw new NotSupportedException(
                $"Auto host binding on '{method.ToDisplayString()}' must declare explicit effects.");
        }

        var effectNames = EffectNames(effects).ToArray();
        var hasRead = effectNames.Contains(DotBoxDGenerationNames.Effects.HostStateRead);
        var hasWrite = effectNames.Contains(DotBoxDGenerationNames.Effects.HostStateWrite);
        if (hasRead == hasWrite)
        {
            throw new NotSupportedException(
                $"Auto host binding on '{method.ToDisplayString()}' must declare exactly one of HostStateRead or HostStateWrite.");
        }

        var hasAlloc = effectNames.Contains(DotBoxDGenerationNames.Effects.Alloc);
        if (hasAlloc != returnAllocates)
        {
            throw new NotSupportedException(
                returnAllocates
                    ? $"Auto host binding on '{method.ToDisplayString()}' must declare Alloc because its return shape allocates."
                    : $"Auto host binding on '{method.ToDisplayString()}' must not declare Alloc because its return shape does not allocate.");
        }

        return effectNames;
    }
}
