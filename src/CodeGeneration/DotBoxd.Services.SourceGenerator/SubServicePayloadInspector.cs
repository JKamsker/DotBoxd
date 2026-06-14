using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxd.Services.SourceGenerator;

internal static class SubServicePayloadInspector
{
    private const string DotBoxdServiceAttributeName = "DotBoxd.Services.Attributes.DotBoxdServiceAttribute";

    public static bool ContainsDotBoxdServiceInterface(ITypeSymbol type, CancellationToken ct) =>
        ContainsDotBoxdServiceInterface(
            type,
            ct,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            cache: null);

    public static bool ContainsDotBoxdServiceInterface(
        ITypeSymbol type,
        CancellationToken ct,
        RpcTypeValidationCache cache) =>
        ContainsDotBoxdServiceInterface(
            type,
            ct,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            cache);

    private static bool ContainsDotBoxdServiceInterface(
        ITypeSymbol type,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        RpcTypeValidationCache? cache)
    {
        ct.ThrowIfCancellationRequested();

        if (type is INamedTypeSymbol named)
        {
            return ContainsDotBoxdServiceInterface(named, ct, visitedOriginalDefinitions, cache);
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsDotBoxdServiceInterface(array.ElementType, ct, visitedOriginalDefinitions, cache);
        }

        return false;
    }

    private static bool ContainsDotBoxdServiceInterface(
        INamedTypeSymbol named,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        RpcTypeValidationCache? cache)
    {
        ct.ThrowIfCancellationRequested();

        if (named.TypeKind == TypeKind.Interface && HasDotBoxdServiceAttribute(named, ct))
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

            if (ContainsDotBoxdServiceInterface(arg, ct, visitedOriginalDefinitions, cache))
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
            DtoMembersContainDotBoxdServiceInterface(named, ct, visitedOriginalDefinitions, cache);
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

    private static bool DtoMembersContainDotBoxdServiceInterface(
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
                ContainsDotBoxdServiceInterface(memberType, ct, visitedOriginalDefinitions, cache))
            {
                return true;
            }
        }

        return type.BaseType is not null && ContainsDotBoxdServiceInterface(
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

    private static bool HasDotBoxdServiceAttribute(INamedTypeSymbol type, CancellationToken ct)
    {
        foreach (var attr in type.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.ToDisplayString() == DotBoxdServiceAttributeName)
            {
                return true;
            }
        }

        return false;
    }
}
