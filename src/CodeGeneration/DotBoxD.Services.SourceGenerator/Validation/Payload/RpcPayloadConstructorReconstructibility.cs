using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadConstructorReconstructibility
{
    public static string? GetUnsupportedReason(INamedTypeSymbol type, string role)
    {
        var immutableMembers = new List<ImmutableMember>();
        for (var current = type; current is not null && CanInspectInheritedMembers(current); current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                switch (member)
                {
                    case IPropertySymbol property:
                        var propertyReason = AddImmutableProperty(property, role, immutableMembers);
                        if (propertyReason is not null)
                        {
                            return propertyReason;
                        }

                        break;
                    case IFieldSymbol field:
                        var fieldReason = AddImmutableField(field, role, immutableMembers);
                        if (fieldReason is not null)
                        {
                            return fieldReason;
                        }

                        break;
                }
            }
        }

        return HasSingleConstructorForAll(type, immutableMembers)
            ? null
            : $"{role} members {FormatMemberList(immutableMembers)} must be reconstructible by a single public constructor";
    }

    private static string? AddImmutableProperty(
        IPropertySymbol property,
        string role,
        List<ImmutableMember> immutableMembers)
    {
        if (property.IsStatic ||
            property.Parameters.Length != 0 ||
            property.DeclaredAccessibility != Accessibility.Public)
        {
            return null;
        }

        if (property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
        {
            return $"{role} member '{property.Name}' must expose a public getter so RPC payloads can be reconstructed";
        }

        if (property.SetMethod?.DeclaredAccessibility == Accessibility.Public)
        {
            return null;
        }

        var constructorMatch = GetConstructorParameterMatch(property.ContainingType, property.Name, property.Type);
        if (constructorMatch != ConstructorParameterMatch.ExactType)
        {
            return constructorMatch == ConstructorParameterMatch.NameOnly
                ? $"{role} member '{property.Name}' must match a public constructor parameter with the same type so RPC payloads can be reconstructed"
                : $"{role} member '{property.Name}' must expose a public setter or init, or match a public constructor parameter, so RPC payloads can be reconstructed";
        }

        immutableMembers.Add(new ImmutableMember(property.Name, property.Type));
        return null;
    }

    private static string? AddImmutableField(
        IFieldSymbol field,
        string role,
        List<ImmutableMember> immutableMembers)
    {
        if (field.IsStatic ||
            field.IsImplicitlyDeclared ||
            field.DeclaredAccessibility != Accessibility.Public ||
            !field.IsReadOnly)
        {
            return null;
        }

        var constructorMatch = GetConstructorParameterMatch(field.ContainingType, field.Name, field.Type);
        if (constructorMatch != ConstructorParameterMatch.ExactType)
        {
            return constructorMatch == ConstructorParameterMatch.NameOnly
                ? $"{role} member '{field.Name}' is readonly and does not match a public constructor parameter with the same type; RPC DTO fields must be reconstructible"
                : $"{role} member '{field.Name}' is readonly and does not match a public constructor parameter; RPC DTO fields must be reconstructible";
        }

        immutableMembers.Add(new ImmutableMember(field.Name, field.Type));
        return null;
    }

    private static ConstructorParameterMatch GetConstructorParameterMatch(
        INamedTypeSymbol type,
        string memberName,
        ITypeSymbol memberType)
    {
        var foundNameMatch = false;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public ||
                constructor.IsStatic)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (string.Equals(parameter.Name, memberName, StringComparison.OrdinalIgnoreCase))
                {
                    foundNameMatch = true;
                    if (SymbolEqualityComparer.Default.Equals(parameter.Type, memberType))
                    {
                        return ConstructorParameterMatch.ExactType;
                    }
                }
            }
        }

        return foundNameMatch ? ConstructorParameterMatch.NameOnly : ConstructorParameterMatch.None;
    }

    private static bool HasSingleConstructorForAll(INamedTypeSymbol type, IReadOnlyList<ImmutableMember> members)
    {
        if (members.Count == 0)
        {
            return true;
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public &&
                !constructor.IsStatic &&
                ConstructorMatchesAll(constructor, members))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConstructorMatchesAll(IMethodSymbol constructor, IReadOnlyList<ImmutableMember> members)
    {
        foreach (var member in members)
        {
            var found = false;
            foreach (var parameter in constructor.Parameters)
            {
                if (string.Equals(parameter.Name, member.Name, StringComparison.OrdinalIgnoreCase) &&
                    SymbolEqualityComparer.Default.Equals(parameter.Type, member.Type))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatMemberList(IReadOnlyList<ImmutableMember> members)
    {
        var names = new string[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            names[i] = $"'{members[i].Name}'";
        }

        return string.Join(", ", names);
    }

    private static bool CanInspectInheritedMembers(INamedTypeSymbol type)
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

    private enum ConstructorParameterMatch
    {
        None,
        NameOnly,
        ExactType,
    }

    private readonly record struct ImmutableMember(string Name, ITypeSymbol Type);
}
