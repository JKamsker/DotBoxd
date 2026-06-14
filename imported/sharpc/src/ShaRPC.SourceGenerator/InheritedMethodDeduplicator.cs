using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class InheritedMethodDeduplicator
{
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";

    public static bool HasCompatibleReturnShape(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct) =>
        left.RefKind == right.RefKind &&
        MethodSignatureFacts.GetCanonicalType(left.ReturnType, left, ct) ==
        MethodSignatureFacts.GetCanonicalType(right.ReturnType, right, ct);

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

    public static bool HasSameNullableAnnotations(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct)
    {
        if (GetNullableTypeKey(left.ReturnType, left, ct) !=
            GetNullableTypeKey(right.ReturnType, right, ct) ||
            left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (GetNullableTypeKey(left.Parameters[i].Type, left, ct) !=
                GetNullableTypeKey(right.Parameters[i].Type, right, ct))
            {
                return false;
            }
        }

        return true;
    }

    public static string GetNullableTypeKey(
        ITypeSymbol type,
        IMethodSymbol method,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is ITypeParameterSymbol typeParameter &&
            typeParameter.TypeParameterKind == TypeParameterKind.Method)
        {
            return "!!" + typeParameter.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                NullableSuffix(typeParameter.NullableAnnotation);
        }

        if (type.TypeKind == TypeKind.Dynamic)
        {
            return "global::System.Object" + NullableSuffix(type.NullableAnnotation);
        }

        if (type is IArrayTypeSymbol array)
        {
            return GetNullableTypeKey(array.ElementType, method, ct) +
                "[" + new string(',', array.Rank - 1) + "]" +
                NullableSuffix(array.NullableAnnotation);
        }

        if (type is INamedTypeSymbol named)
        {
            return GetNullableNamedTypeKey(named, method, ct);
        }

        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            NullableSuffix(type.NullableAnnotation);
    }

    private static string GetNullableNamedTypeKey(
        INamedTypeSymbol type,
        IMethodSymbol method,
        CancellationToken ct)
    {
        var name = type.ContainingType is null
            ? GetNamespacePrefix(type) + type.MetadataName
            : GetNullableNamedTypeKey(type.ContainingType, method, ct) + "." + type.MetadataName;
        name += NullableSuffix(type.NullableAnnotation);
        if (type.TypeArguments.Length == 0)
        {
            return name;
        }

        var args = new List<string>();
        foreach (var arg in type.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();
            args.Add(GetNullableTypeKey(arg, method, ct));
        }

        return name + "<" + string.Join(",", args) + ">";
    }

    private static string GetNamespacePrefix(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace
            ? "global::"
            : "global::" + type.ContainingNamespace.ToDisplayString() + ".";

    private static string NullableSuffix(NullableAnnotation annotation) =>
        annotation == NullableAnnotation.Annotated ? "?" : string.Empty;

    public static bool HasSameEffectiveWireName(IMethodSymbol left, IMethodSymbol right) =>
        GetEffectiveWireName(left) == GetEffectiveWireName(right);

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

    private static string GetEffectiveWireName(IMethodSymbol methodSymbol) =>
        GetConfiguredMethodName(methodSymbol) ?? methodSymbol.Name;

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
