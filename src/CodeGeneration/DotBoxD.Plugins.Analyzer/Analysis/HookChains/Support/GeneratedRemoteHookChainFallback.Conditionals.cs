using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static GeneratedRemoteHookChainTarget? TargetFromConditionalRegistryExpression(
        ConditionalExpressionSyntax conditional,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        var whenTrue = RegistryTarget(conditional.WhenTrue, model, cancellationToken, depth + 1);
        if (whenTrue is null)
        {
            return null;
        }

        var whenFalse = RegistryTarget(conditional.WhenFalse, model, cancellationToken, depth + 1);
        if (whenFalse is null ||
            !whenTrue.Value.Equals(whenFalse.Value))
        {
            return null;
        }

        return whenTrue;
    }
}
