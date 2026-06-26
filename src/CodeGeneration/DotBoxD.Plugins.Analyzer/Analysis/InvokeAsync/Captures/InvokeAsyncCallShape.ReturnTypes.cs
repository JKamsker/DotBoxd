using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static bool TryGeneratedReceiverReturnType(
        InvocationExpressionSyntax invocation,
        BlockSyntax block,
        SemanticModel model,
        CancellationToken cancellationToken,
        int expectedTypeArgumentCount,
        int typeArgumentIndex,
        out ITypeSymbol returnType)
    {
        if (TryExplicitGenericTypeArgument(
                invocation,
                model,
                cancellationToken,
                expectedTypeArgumentCount,
                typeArgumentIndex,
                out returnType))
        {
            return true;
        }

        return InvokeAsyncLambdaShape.TryReturnType(block, model, cancellationToken, out returnType) ||
               TryContextReturnType(invocation, model, cancellationToken, out returnType);
    }

    private static bool TryContextReturnType(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ITypeSymbol returnType)
    {
        returnType = null!;
        if (invocation.Parent is not ArrowExpressionClauseSyntax arrow ||
            arrow.Parent is not MethodDeclarationSyntax method ||
            model.GetTypeInfo(method.ReturnType, cancellationToken).Type is not { } methodReturn ||
            DotBoxDRpcReturnType.PayloadType(methodReturn) is not { } payload)
        {
            return false;
        }

        returnType = payload;
        return true;
    }

    private static bool TryExplicitGenericTypeArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        int expectedTypeArgumentCount,
        int typeArgumentIndex,
        out ITypeSymbol type)
    {
        type = null!;
        if (GenericInvokeAsyncName(invocation.Expression) is not { } generic ||
            generic.TypeArgumentList.Arguments.Count != expectedTypeArgumentCount)
        {
            return false;
        }

        var candidate = model.GetTypeInfo(
            generic.TypeArgumentList.Arguments[typeArgumentIndex],
            cancellationToken).Type;
        if (candidate is null || candidate.TypeKind == TypeKind.Error)
        {
            return false;
        }

        type = candidate;
        return true;
    }

    private static GenericNameSyntax? GenericInvokeAsyncName(ExpressionSyntax expression)
        => expression switch
        {
            GenericNameSyntax { Identifier.ValueText: "InvokeAsync" } generic => generic,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "InvokeAsync" } generic } => generic,
            _ => null,
        };

    private static string BuildReturnTypeJson(ITypeSymbol returnType, IReadOnlyList<InvokeAsyncSyncOut> syncOuts)
    {
        if (syncOuts.Count == 0)
        {
            return DotBoxDRpcReturnType.JsonType(returnType);
        }

        var fields = new string[1 + syncOuts.Count];
        fields[0] = DotBoxDRpcReturnType.JsonType(returnType);
        for (var i = 0; i < syncOuts.Count; i++)
        {
            fields[i + 1] = DotBoxDRpcTypeMapper.JsonType(syncOuts[i].Type);
        }

        return "{\"name\":\"Record\",\"arguments\":[" + string.Join(",", fields) + "]}";
    }
}

internal sealed record InvokeAsyncCaptureParameter(string Name, INamedTypeSymbol Type);

internal sealed record InvokeAsyncSyncOut(
    string TargetName,
    ITypeSymbol Type,
    string LocalName,
    ExpressionSyntax? Initializer);
