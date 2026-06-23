using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static bool TerminalReturnsVoid(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda.ExpressionBody is not { } body)
        {
            return false;
        }

        if (model.GetTypeInfo(body, cancellationToken).Type?.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        return model.GetSymbolInfo(body, cancellationToken).Symbol is IMethodSymbol { ReturnsVoid: true };
    }
}
