using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class MethodSignatureFacts
{
    public static string GetSignatureKey(IMethodSymbol method, CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            ct.ThrowIfCancellationRequested();
            parts.Add(GetCanonicalParameterRefKind(parameter.RefKind) + GetCanonicalType(parameter.Type, method, ct));
        }

        return method.Name + "`" + method.Arity + "(" + string.Join(",", parts) + ")";
    }

    public static string GetSignatureKey(
        string methodName,
        int arity,
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct)
    {
        var sb = new StringBuilder(IdentifierHelpers.UnescapeIdentifier(methodName));
        sb.Append('`').Append(arity).Append('(');
        for (var i = 0; i < parameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(GetCanonicalParameterRefKind(parameters[i].RefKindKeyword))
                .Append(parameters[i].SignatureType);
        }

        return sb.Append(')').ToString();
    }

    public static string GetCanonicalType(ITypeSymbol type, IMethodSymbol method, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is ITypeParameterSymbol typeParameter &&
            typeParameter.TypeParameterKind == TypeParameterKind.Method)
        {
            return "!!" + typeParameter.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type.TypeKind == TypeKind.Dynamic)
        {
            return "global::System.Object";
        }

        if (type is IArrayTypeSymbol array)
        {
            return GetCanonicalType(array.ElementType, method, ct) + "[" + new string(',', array.Rank - 1) + "]";
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.IsTupleType && named.TupleUnderlyingType is { } tupleUnderlyingType)
            {
                return GetCanonicalNamedType(tupleUnderlyingType, method, ct);
            }

            return GetCanonicalNamedType(named, method, ct);
        }

        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static bool HaveSameGenericConstraints(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct)
    {
        if (left.Arity != right.Arity)
        {
            return false;
        }

        for (var i = 0; i < left.TypeParameters.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!HaveSameConstraints(left.TypeParameters[i], left, right.TypeParameters[i], right, ct))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveSameConstraints(
        ITypeParameterSymbol left,
        IMethodSymbol leftMethod,
        ITypeParameterSymbol right,
        IMethodSymbol rightMethod,
        CancellationToken ct)
    {
        if (left.HasReferenceTypeConstraint != right.HasReferenceTypeConstraint ||
            left.ReferenceTypeConstraintNullableAnnotation != right.ReferenceTypeConstraintNullableAnnotation ||
            left.HasValueTypeConstraint != right.HasValueTypeConstraint ||
            left.HasUnmanagedTypeConstraint != right.HasUnmanagedTypeConstraint ||
            left.HasNotNullConstraint != right.HasNotNullConstraint ||
            left.HasConstructorConstraint != right.HasConstructorConstraint ||
            left.ConstraintTypes.Length != right.ConstraintTypes.Length)
        {
            return false;
        }

        var leftConstraints = GetCanonicalConstraintTypes(left.ConstraintTypes, leftMethod, ct);
        var rightConstraints = GetCanonicalConstraintTypes(right.ConstraintTypes, rightMethod, ct);
        for (var i = 0; i < leftConstraints.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (leftConstraints[i] != rightConstraints[i])
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> GetCanonicalConstraintTypes(
        ImmutableArray<ITypeSymbol> constraintTypes,
        IMethodSymbol method,
        CancellationToken ct)
    {
        var constraints = new List<string>(constraintTypes.Length);
        foreach (var constraintType in constraintTypes)
        {
            ct.ThrowIfCancellationRequested();
            constraints.Add(InheritedMethodDeduplicator.GetNullableTypeKey(constraintType, method, ct));
        }

        constraints.Sort(System.StringComparer.Ordinal);
        return constraints;
    }

    private static string GetCanonicalNamedType(
        INamedTypeSymbol type,
        IMethodSymbol method,
        CancellationToken ct)
    {
        var name = type.ContainingType is null
            ? GetNamespacePrefix(type) + type.MetadataName
            : GetCanonicalNamedType(type.ContainingType, method, ct) + "." + type.MetadataName;
        if (type.TypeArguments.Length == 0)
        {
            return name;
        }

        var args = new List<string>();
        foreach (var arg in type.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();
            args.Add(GetCanonicalType(arg, method, ct));
        }

        return name + "<" + string.Join(",", args) + ">";
    }

    private static string GetNamespacePrefix(INamedTypeSymbol type)
    {
        var prefix = type.ContainingNamespace.IsGlobalNamespace
            ? "global::"
            : "global::" + type.ContainingNamespace.ToDisplayString() + ".";
        return prefix;
    }

    private static string GetCanonicalParameterRefKind(RefKind kind) =>
        kind == RefKind.None ? string.Empty : "ref ";

    private static string GetCanonicalParameterRefKind(string refKindKeyword) =>
        string.IsNullOrEmpty(refKindKeyword) ? string.Empty : "ref ";
}
