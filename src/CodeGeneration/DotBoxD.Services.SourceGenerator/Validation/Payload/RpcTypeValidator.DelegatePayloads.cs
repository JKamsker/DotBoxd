using System;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static partial class RpcTypeValidator
{
    private static bool ContainsDelegatePayloadType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return ContainsDelegatePayloadType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.TypeKind == TypeKind.Delegate || IsDelegateBaseType(named))
            {
                return true;
            }

            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsDelegatePayloadType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsDelegateBaseType(INamedTypeSymbol type) =>
        (type.Name == nameof(Delegate) || type.Name == nameof(MulticastDelegate)) &&
        type.ContainingNamespace?.ToDisplayString() == "System";
}
