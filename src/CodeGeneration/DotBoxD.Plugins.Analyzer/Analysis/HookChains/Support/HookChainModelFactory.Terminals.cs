using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static bool TerminalReturnsVoid(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (ConvertedLambdaReturnType(lambda, model, cancellationToken) is { } convertedReturn)
        {
            return convertedReturn.SpecialType == SpecialType.System_Void;
        }

        if (ExplicitLambdaReturnType(lambda, model, cancellationToken) is { } explicitReturn)
        {
            return explicitReturn.SpecialType == SpecialType.System_Void;
        }

        if (lambda.ExpressionBody is not { } body)
        {
            return lambda.Body is BlockSyntax block && BlockReturnsVoid(lambda, block);
        }

        if (model.GetTypeInfo(body, cancellationToken).Type?.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        return model.GetSymbolInfo(body, cancellationToken).Symbol is IMethodSymbol { ReturnsVoid: true };
    }

    private static ITypeSymbol? ConvertedLambdaReturnType(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(lambda, cancellationToken).ConvertedType is not INamedTypeSymbol convertedType)
        {
            return null;
        }

        return convertedType.DelegateInvokeMethod?.ReturnType;
    }

    private static ITypeSymbol? ExplicitLambdaReturnType(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda is not ParenthesizedLambdaExpressionSyntax { ReturnType: { } returnSyntax } ||
            returnSyntax.IsMissing)
        {
            return null;
        }

        var returnType = model.GetTypeInfo(returnSyntax, cancellationToken).Type;
        if (returnType is not null && returnType.TypeKind != TypeKind.Error)
        {
            return returnType;
        }

        return returnSyntax is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }
            ? model.Compilation.GetSpecialType(SpecialType.System_Void)
            : null;
    }

    private static bool BlockReturnsVoid(LambdaExpressionSyntax lambda, BlockSyntax block)
    {
        if (lambda.AsyncKeyword.RawKind != 0)
        {
            return false;
        }

        return !block.DescendantNodes(static node =>
                node is not LambdaExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<ReturnStatementSyntax>()
            .Any(static returned => returned.Expression is not null);
    }
}
