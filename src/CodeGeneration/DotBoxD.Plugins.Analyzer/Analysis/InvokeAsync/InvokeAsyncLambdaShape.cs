using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncLambdaShape
{
    internal static bool TryLambda(ExpressionSyntax expression, out LambdaExpressionSyntax lambda)
    {
        lambda = null!;
        if (expression is not LambdaExpressionSyntax found)
        {
            return false;
        }

        lambda = found;
        return true;
    }

    internal static bool TryWorldParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ITypeSymbol worldType)
        => TryWorldParameter(lambda, model, cancellationToken, expectedWorldType: null, out worldType);

    internal static bool TryWorldParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken,
        ITypeSymbol? expectedWorldType,
        out ITypeSymbol worldType)
    {
        worldType = null!;
        if (LambdaParameterTypes(lambda, model, cancellationToken) is not { Count: 1 } parameterTypes)
        {
            if (expectedWorldType is null || LambdaParameterCount(lambda) != 1)
            {
                return false;
            }

            worldType = expectedWorldType;
            return true;
        }

        worldType = parameterTypes[0];
        if (worldType.TypeKind == TypeKind.Error &&
            expectedWorldType is not null &&
            HasImplicitSingleParameter(lambda))
        {
            worldType = expectedWorldType;
            return true;
        }

        return MatchesExpectedWorld(worldType, expectedWorldType);
    }

    internal static bool TryCaptureParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        ExpressionSyntax captureArgument,
        CancellationToken cancellationToken,
        out InvokeAsyncCaptureParameter parameter,
        out ITypeSymbol worldType)
        => TryCaptureParameter(
            lambda,
            model,
            captureArgument,
            cancellationToken,
            expectedWorldType: null,
            expectedCaptureType: null,
            out parameter,
            out worldType);

    internal static bool TryCaptureParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        ExpressionSyntax captureArgument,
        CancellationToken cancellationToken,
        ITypeSymbol? expectedWorldType,
        ITypeSymbol? expectedCaptureType,
        out InvokeAsyncCaptureParameter parameter,
        out ITypeSymbol worldType)
    {
        parameter = null!;
        worldType = null!;
        if (lambda is not ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } parenthesized ||
            LambdaParameterTypes(lambda, model, cancellationToken) is not { Count: 2 } parameterTypes ||
            CaptureType(model, captureArgument, expectedCaptureType, cancellationToken) is not INamedTypeSymbol captureType ||
            DotBoxDRpcTypeMapper.RecordFields(captureType).Count == 0)
        {
            return false;
        }

        worldType = parameterTypes[0];
        if (!MatchesExpectedWorld(worldType, expectedWorldType))
        {
            return false;
        }

        var captureSyntax = parenthesized.ParameterList.Parameters[1];
        var declaredType = captureSyntax.Type is null
            ? null
            : model.GetTypeInfo(captureSyntax.Type, cancellationToken).Type;
        if (declaredType is not null &&
            !SymbolEqualityComparer.Default.Equals(declaredType, captureType))
        {
            return false;
        }

        if (!SymbolEqualityComparer.Default.Equals(parameterTypes[1], captureType))
        {
            return false;
        }

        parameter = new InvokeAsyncCaptureParameter(captureSyntax.Identifier.ValueText, captureType);
        return true;
    }

    internal static string WorldParameterName(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: > 0 } parenthesized =>
                parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            _ => string.Empty
        };

    private static ITypeSymbol? CaptureType(
        SemanticModel model,
        ExpressionSyntax captureArgument,
        ITypeSymbol? expectedCaptureType,
        CancellationToken cancellationToken)
    {
        if (expectedCaptureType is not null)
        {
            var conversion = model.ClassifyConversion(captureArgument, expectedCaptureType);
            return conversion.Exists && conversion.IsImplicit ? expectedCaptureType : null;
        }

        return model.GetTypeInfo(captureArgument, cancellationToken).Type;
    }

    internal static bool TryReturnType(
        BlockSyntax block,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ITypeSymbol returnType)
    {
        returnType = null!;
        foreach (var statement in block.DescendantNodes().OfType<ReturnStatementSyntax>())
        {
            if (statement.Expression is null ||
                model.GetTypeInfo(statement.Expression, cancellationToken).Type is not { TypeKind: not TypeKind.Error } current)
            {
                return false;
            }

            if (returnType is null)
            {
                returnType = current;
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(returnType, current))
            {
                return false;
            }
        }

        return returnType is not null;
    }

    private static IReadOnlyList<ITypeSymbol>? LambdaParameterTypes(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(lambda, cancellationToken).ConvertedType is INamedTypeSymbol
            {
                DelegateInvokeMethod: { } invoke
            })
        {
            return invoke.Parameters.Select(parameter => parameter.Type).ToArray();
        }

        return ExplicitLambdaParameterTypes(lambda, model, cancellationToken);
    }

    private static int LambdaParameterCount(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.Count,
            SimpleLambdaExpressionSyntax => 1,
            _ => 0
        };

    private static bool HasImplicitSingleParameter(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax { Parameter.Type: null } => true,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized =>
                parenthesized.ParameterList.Parameters[0].Type is null,
            _ => false
        };

    private static IReadOnlyList<ITypeSymbol>? ExplicitLambdaParameterTypes(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var parameterTypes = lambda switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters
                .Select(parameter => ParameterType(parameter, model, cancellationToken))
                .ToArray(),
            SimpleLambdaExpressionSyntax simple => [ParameterType(simple.Parameter, model, cancellationToken)],
            _ => []
        };
        if (parameterTypes.Length == 0)
        {
            return null;
        }

        var resolved = new ITypeSymbol[parameterTypes.Length];
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            if (parameterTypes[i] is not { } parameterType)
            {
                return null;
            }

            resolved[i] = parameterType;
        }

        return resolved;
    }

    private static ITypeSymbol? ParameterType(
        ParameterSyntax parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
        => parameter.Type is { } type
            ? model.GetTypeInfo(type, cancellationToken).Type
            : (model.GetDeclaredSymbol(parameter, cancellationToken) as IParameterSymbol)?.Type;

    private static bool MatchesExpectedWorld(ITypeSymbol worldType, ITypeSymbol? expectedWorldType)
        => expectedWorldType is null ||
           SymbolEqualityComparer.Default.Equals(worldType, expectedWorldType);
}
