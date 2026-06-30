using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal delegate bool DtoPayloadTypePredicate(
    ITypeSymbol type,
    CancellationToken ct,
    HashSet<INamedTypeSymbol> visitedOriginalDefinitions);

internal delegate string? DtoPayloadTypeReasonSelector(
    ITypeSymbol type,
    string role,
    CancellationToken ct,
    HashSet<INamedTypeSymbol> visitedOriginalDefinitions);

internal static class DtoPayloadMemberInspector
{
    public static bool ContainsMemberMatching(
        INamedTypeSymbol type,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        DtoPayloadTypePredicate contains)
    {
        if (!CanInspectDtoMembers(type) || !visitedOriginalDefinitions.Add(type.OriginalDefinition))
        {
            return false;
        }

        try
        {
            foreach (var member in type.GetMembers())
            {
                ct.ThrowIfCancellationRequested();

                if (MemberType(member) is { } memberType &&
                    contains(memberType, ct, visitedOriginalDefinitions))
                {
                    return true;
                }
            }

            return type.BaseType is not null &&
                ContainsMemberMatching(type.BaseType, ct, visitedOriginalDefinitions, contains);
        }
        finally
        {
            visitedOriginalDefinitions.Remove(type.OriginalDefinition);
        }
    }

    public static string? FindUnsupportedMember(
        INamedTypeSymbol type,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        DtoPayloadTypeReasonSelector getReason)
    {
        if (!CanInspectDtoMembers(type) || !visitedOriginalDefinitions.Add(type.OriginalDefinition))
        {
            return null;
        }

        try
        {
            foreach (var member in type.GetMembers())
            {
                ct.ThrowIfCancellationRequested();

                if (MemberType(member) is not { } memberType)
                {
                    continue;
                }

                var memberRole = $"{role} member '{member.Name}'";
                var reason = getReason(memberType, memberRole, ct, visitedOriginalDefinitions);
                if (reason is not null)
                {
                    return reason;
                }
            }

            return type.BaseType is null
                ? null
                : FindUnsupportedMember(type.BaseType, role, ct, visitedOriginalDefinitions, getReason);
        }
        finally
        {
            visitedOriginalDefinitions.Remove(type.OriginalDefinition);
        }
    }

    private static ITypeSymbol? MemberType(ISymbol member)
        => member switch
        {
            IPropertySymbol { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public } property => property.Type,
            IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public } field => field.Type,
            _ => null,
        };

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
}

internal static class RpcPayloadMemberInspector
{
    public static string? GetUnsupportedPayloadMemberReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        bool allowTopLevelAsyncWrapper,
        bool allowCurrentTransportShape,
        bool allowCurrentCancellationToken,
        ITypeSymbol? cancellationTokenSymbol) =>
        Inspect(
            type,
            role,
            ct,
            allowTopLevelAsyncWrapper,
            allowCurrentTransportShape,
            allowCurrentCancellationToken,
            cancellationTokenSymbol,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));

    private static string? Inspect(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        bool allowCurrentTaskWrapper,
        bool allowCurrentTransportShape,
        bool allowCurrentCancellationToken,
        ITypeSymbol? cancellationTokenSymbol,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return Inspect(
                array.ElementType,
                role,
                ct,
                allowCurrentTaskWrapper: false,
                allowCurrentTransportShape: false,
                allowCurrentCancellationToken: false,
                cancellationTokenSymbol,
                visitedOriginalDefinitions);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        if (IsTaskLike(named) && allowCurrentTaskWrapper)
        {
            return InspectTypeArguments(
                named,
                role,
                ct,
                allowCurrentTransportShape,
                allowCurrentCancellationToken: false,
                cancellationTokenSymbol,
                visitedOriginalDefinitions);
        }

        var argumentReason = InspectTypeArguments(
            named,
            role,
            ct,
            allowCurrentTransportShape: false,
            allowCurrentCancellationToken: false,
            cancellationTokenSymbol,
            visitedOriginalDefinitions);
        if (argumentReason is not null)
        {
            return argumentReason;
        }

        return DtoPayloadMemberInspector.FindUnsupportedMember(
            named,
            role,
            ct,
            visitedOriginalDefinitions,
            (memberType, memberRole, memberCt, memberVisited) =>
                UnsupportedMemberReason(memberType, memberRole, memberCt, cancellationTokenSymbol, memberVisited));
    }

    private static string? InspectTypeArguments(
        INamedTypeSymbol named,
        string role,
        CancellationToken ct,
        bool allowCurrentTransportShape,
        bool allowCurrentCancellationToken,
        ITypeSymbol? cancellationTokenSymbol,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        foreach (var arg in named.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();

            var directReason = RpcTypeValidator.GetUnsupportedDirectTypeReason(
                arg,
                role,
                ct,
                allowTopLevelAsyncWrapper: false,
                allowCurrentTransportShape,
                allowCurrentCancellationToken,
                cancellationTokenSymbol: cancellationTokenSymbol);
            if (directReason is not null)
            {
                return directReason;
            }

            var memberReason = Inspect(
                arg,
                role,
                ct,
                allowCurrentTaskWrapper: false,
                allowCurrentTransportShape,
                allowCurrentCancellationToken,
                cancellationTokenSymbol,
                visitedOriginalDefinitions);
            if (memberReason is not null)
            {
                return memberReason;
            }
        }

        return null;
    }

    private static string? UnsupportedMemberReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        ITypeSymbol? cancellationTokenSymbol,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        var directReason = RpcTypeValidator.GetUnsupportedDirectTypeReason(
            type,
            role,
            ct,
            allowTopLevelAsyncWrapper: false,
            cancellationTokenSymbol: cancellationTokenSymbol);
        return directReason ?? Inspect(
            type,
            role,
            ct,
            allowCurrentTaskWrapper: false,
            allowCurrentTransportShape: false,
            allowCurrentCancellationToken: false,
            cancellationTokenSymbol,
            visitedOriginalDefinitions);
    }

    private static bool IsTaskLike(INamedTypeSymbol type)
        => (type.Name == "Task" || type.Name == "ValueTask") &&
            type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
}
