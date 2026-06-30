using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class SubServicePayloadInspector
{
    private const string DotBoxDServiceAttributeName = ServicesGeneratorTypeNames.DotBoxDServiceAttribute;

    public static bool ContainsDotBoxDServiceInterface(ITypeSymbol type, CancellationToken ct) =>
        ContainsDotBoxDServiceInterface(
            type,
            ct,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            cache: null);

    public static bool ContainsDotBoxDServiceInterface(
        ITypeSymbol type,
        CancellationToken ct,
        RpcTypeValidationCache cache) =>
        ContainsDotBoxDServiceInterface(
            type,
            ct,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            cache);

    private static bool ContainsDotBoxDServiceInterface(
        ITypeSymbol type,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        RpcTypeValidationCache? cache)
    {
        ct.ThrowIfCancellationRequested();

        if (type is INamedTypeSymbol named)
        {
            return ContainsDotBoxDServiceInterface(named, ct, visitedOriginalDefinitions, cache);
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsDotBoxDServiceInterface(array.ElementType, ct, visitedOriginalDefinitions, cache);
        }

        return false;
    }

    private static bool ContainsDotBoxDServiceInterface(
        INamedTypeSymbol named,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        RpcTypeValidationCache? cache)
    {
        ct.ThrowIfCancellationRequested();

        if (named.TypeKind == TypeKind.Interface && HasDotBoxDServiceAttribute(named, ct))
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

            if (ContainsDotBoxDServiceInterface(arg, ct, visitedOriginalDefinitions, cache))
            {
                if (cache is not null)
                {
                    cache.SetSubServicePayloadResult(named, result: true);
                }

                return true;
            }
        }

        var contains = DtoPayloadMemberInspector.ContainsMemberMatching(
            named,
            ct,
            visitedOriginalDefinitions,
            (memberType, memberCt, memberVisited) =>
                ContainsDotBoxDServiceInterface(memberType, memberCt, memberVisited, cache));

        // Only positive results are cycle-independent and safe to cache. A false computed here may
        // be a cycle-break artifact (the type's traversal hit an in-progress ancestor and returned
        // false), so caching it could poison a later independent lookup of the same type.
        if (cache is not null && contains)
        {
            cache.SetSubServicePayloadResult(named, result: true);
        }

        return contains;
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
}
