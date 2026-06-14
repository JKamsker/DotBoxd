using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class SubServicePayloadInspector
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";

    public static bool ContainsShaRpcServiceInterface(ITypeSymbol type, CancellationToken ct) =>
        ContainsShaRpcServiceInterface(
            type,
            ct,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            cache: null);

    public static bool ContainsShaRpcServiceInterface(
        ITypeSymbol type,
        CancellationToken ct,
        RpcTypeValidationCache cache) =>
        ContainsShaRpcServiceInterface(
            type,
            ct,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            cache);

    private static bool ContainsShaRpcServiceInterface(
        ITypeSymbol type,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        RpcTypeValidationCache? cache)
    {
        ct.ThrowIfCancellationRequested();

        if (type is INamedTypeSymbol named)
        {
            return ContainsShaRpcServiceInterface(named, ct, visitedOriginalDefinitions, cache);
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsShaRpcServiceInterface(array.ElementType, ct, visitedOriginalDefinitions, cache);
        }

        return false;
    }

    private static bool ContainsShaRpcServiceInterface(
        INamedTypeSymbol named,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        RpcTypeValidationCache? cache)
    {
        ct.ThrowIfCancellationRequested();

        if (named.TypeKind == TypeKind.Interface && HasShaRpcServiceAttribute(named, ct))
        {
            return true;
        }

        if (cache is not null && cache.TryGetSubServicePayloadResult(named, out var cached))
        {
            return cached;
        }

        foreach (var arg in named.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();

            if (ContainsShaRpcServiceInterface(arg, ct, visitedOriginalDefinitions, cache))
            {
                if (cache is not null)
                {
                    cache.SetSubServicePayloadResult(named, result: true);
                }

                return true;
            }
        }

        if (!visitedOriginalDefinitions.Add(named.OriginalDefinition))
        {
            return false;
        }

        var contains = CanInspectDtoMembers(named) &&
            DtoMembersContainShaRpcServiceInterface(named, ct, visitedOriginalDefinitions, cache);
        visitedOriginalDefinitions.Remove(named.OriginalDefinition);

        // Only positive results are cycle-independent and safe to cache. A false computed here may
        // be a cycle-break artifact (the type's traversal hit an in-progress ancestor and returned
        // false), so caching it could poison a later independent lookup of the same type.
        if (cache is not null && contains)
        {
            cache.SetSubServicePayloadResult(named, result: true);
        }

        return contains;
    }

    private static bool DtoMembersContainShaRpcServiceInterface(
        INamedTypeSymbol type,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        RpcTypeValidationCache? cache)
    {
        foreach (var member in type.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            var memberType = member switch
            {
                IPropertySymbol { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public } property => property.Type,
                IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public } field => field.Type,
                _ => null,
            };

            if (memberType is not null &&
                ContainsShaRpcServiceInterface(memberType, ct, visitedOriginalDefinitions, cache))
            {
                return true;
            }
        }

        return type.BaseType is not null && ContainsShaRpcServiceInterface(
            type.BaseType,
            ct,
            visitedOriginalDefinitions,
            cache);
    }

    private static bool CanInspectDtoMembers(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return false;
        }

        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace || !IsSystemNamespace(ns);
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

    private static bool HasShaRpcServiceAttribute(INamedTypeSymbol type, CancellationToken ct)
    {
        foreach (var attr in type.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName)
            {
                return true;
            }
        }

        return false;
    }
}
