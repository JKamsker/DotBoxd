using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class InheritedMethodDeduplicator
{
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";

    public static bool HasCompatibleReturnShape(IMethodSymbol left, IMethodSymbol right) =>
        left.RefKind == right.RefKind &&
        SymbolEqualityComparer.Default.Equals(left.ReturnType, right.ReturnType);

    public static bool HasSameParameterRefKinds(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (left.Parameters[i].RefKind != right.Parameters[i].RefKind)
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasSameConfiguredWireName(IMethodSymbol left, IMethodSymbol right) =>
        GetConfiguredMethodName(left) == GetConfiguredMethodName(right);

    public static MethodModel AddAdditionalExplicitImplementation(
        MethodModel method,
        INamedTypeSymbol implementationType)
    {
        var typeName = MethodModelFactory.GetExplicitImplementationType(implementationType);
        var types = new List<string>();
        foreach (var type in method.AdditionalExplicitImplementationTypes)
        {
            types.Add(type);
        }

        if (!types.Contains(typeName))
        {
            types.Add(typeName);
        }

        return method with
        {
            AdditionalExplicitImplementationTypes = types.ToEquatableArray(),
            RequiresDispatcherReceiverCast = true,
        };
    }

    private static string? GetConfiguredMethodName(IMethodSymbol methodSymbol)
    {
        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != ShaRpcMethodAttributeName)
            {
                continue;
            }

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                {
                    return s;
                }
            }
        }

        return null;
    }
}
