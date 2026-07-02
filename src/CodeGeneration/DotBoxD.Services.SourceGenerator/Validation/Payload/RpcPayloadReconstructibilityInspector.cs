using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadReconstructibilityInspector
{
    private const string DotBoxDServiceAttributeName = ServicesGeneratorTypeNames.DotBoxDServiceAttribute;

    public static string? GetUnsupportedReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct) =>
        Inspect(
            type,
            role,
            ct,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            requireConstructible: true);

    private static string? Inspect(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        bool requireConstructible)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return Inspect(array.ElementType, role, ct, visitedOriginalDefinitions, requireConstructible: true);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        foreach (var arg in named.TypeArguments)
        {
            var argumentReason = Inspect(arg, role, ct, visitedOriginalDefinitions, requireConstructible: true);
            if (argumentReason is not null)
            {
                return argumentReason;
            }
        }

        if (requireConstructible)
        {
            var constructibilityReason = GetNonConstructibleDtoReason(named, role, ct);
            if (constructibilityReason is not null)
            {
                return constructibilityReason;
            }
        }

        if (!CanInspectDtoMembers(named) || !visitedOriginalDefinitions.Add(named.OriginalDefinition))
        {
            return null;
        }

        try
        {
            var reconstructibilityReason = RpcPayloadConstructorReconstructibility.GetUnsupportedReason(named, role);
            if (reconstructibilityReason is not null)
            {
                return reconstructibilityReason;
            }

            foreach (var member in named.GetMembers())
            {
                ct.ThrowIfCancellationRequested();

                var reason = member switch
                {
                    IPropertySymbol property => InspectProperty(property, role, ct, visitedOriginalDefinitions),
                    IFieldSymbol field => InspectField(field, role, ct, visitedOriginalDefinitions),
                    _ => null,
                };
                if (reason is not null)
                {
                    return reason;
                }
            }

            return named.BaseType is null
                ? null
                : Inspect(named.BaseType, role, ct, visitedOriginalDefinitions, requireConstructible: false);
        }
        finally
        {
            visitedOriginalDefinitions.Remove(named.OriginalDefinition);
        }
    }

    private static string? InspectProperty(
        IPropertySymbol property,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        if (property.IsStatic ||
            property.Parameters.Length != 0 ||
            property.DeclaredAccessibility != Accessibility.Public)
        {
            return null;
        }

        return Inspect(
            property.Type,
            $"{role} member '{property.Name}'",
            ct,
            visitedOriginalDefinitions,
            requireConstructible: true);
    }

    private static string? InspectField(
        IFieldSymbol field,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        if (field.IsStatic ||
            field.IsImplicitlyDeclared ||
            field.DeclaredAccessibility != Accessibility.Public)
        {
            return null;
        }

        return Inspect(
            field.Type,
            $"{role} member '{field.Name}'",
            ct,
            visitedOriginalDefinitions,
            requireConstructible: true);
    }

    private static string? GetNonConstructibleDtoReason(
        INamedTypeSymbol type,
        string role,
        CancellationToken ct)
    {
        if (!IsUserDtoNamespace(type) || HasDotBoxDServiceAttribute(type, ct))
        {
            return null;
        }

        return type.TypeKind switch
        {
            TypeKind.Interface =>
                $"{role} uses interface DTO '{type.ToDisplayString()}'; RPC payload DTOs must be concrete so the wire contract can be reconstructed.",
            TypeKind.Class when type.IsAbstract =>
                $"{role} uses abstract DTO '{type.ToDisplayString()}'; RPC payload DTOs must be concrete so the wire contract can be reconstructed.",
            _ => null,
        };
    }

    private static bool CanInspectDtoMembers(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return false;
        }

        return IsUserDtoNamespace(type);
    }

    private static bool IsUserDtoNamespace(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None)
        {
            return false;
        }

        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace || !IsSystemNamespace(ns);
    }

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type, CancellationToken ct)
    {
        foreach (var attr in type.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.ToDisplayString() == DotBoxDServiceAttributeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSystemNamespace(INamespaceSymbol ns)
    {
        while (!ns.IsGlobalNamespace)
        {
            if (ns.ContainingNamespace.IsGlobalNamespace)
            {
                return ns.Name == "System";
            }

            ns = ns.ContainingNamespace;
        }

        return false;
    }
}
