using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal readonly record struct MethodMetadataTypes(
    string ReturnType,
    string? ResultType);

internal static class MethodMetadataTypesFactory
{
    public static MethodMetadataTypes Get(
        IMethodSymbol method,
        MethodReturnKind returnKind,
        CancellationToken ct)
    {
        var returnType = TypeOfExpressionFormatter.Format(method.ReturnType, ct);
        var resultType = GetResultType(method.ReturnType, returnKind, method, ct);
        return new MethodMetadataTypes(returnType, resultType);
    }

    private static string? GetResultType(
        ITypeSymbol returnType,
        MethodReturnKind returnKind,
        IMethodSymbol method,
        CancellationToken ct)
    {
        if (!HasGenericAsyncResult(returnKind))
        {
            return null;
        }

        if (returnType is not INamedTypeSymbol { TypeArguments.Length: 1 } named)
        {
            return null;
        }

        return TypeOfExpressionFormatter.Format(named.TypeArguments[0], ct);
    }

    private static bool HasGenericAsyncResult(MethodReturnKind returnKind) =>
        returnKind == MethodReturnKind.TaskOf ||
        returnKind == MethodReturnKind.ValueTaskOf ||
        returnKind == MethodReturnKind.TaskOfSubService ||
        returnKind == MethodReturnKind.ValueTaskOfSubService ||
        returnKind == MethodReturnKind.TaskOfAsyncEnumerable ||
        returnKind == MethodReturnKind.ValueTaskOfAsyncEnumerable ||
        returnKind == MethodReturnKind.TaskOfStream ||
        returnKind == MethodReturnKind.ValueTaskOfStream ||
        returnKind == MethodReturnKind.TaskOfPipe ||
        returnKind == MethodReturnKind.ValueTaskOfPipe;
}
