using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ShaRPC.SourceGenerator;

internal sealed record ExistingTypeIndex(EquatableArray<ExistingTypeKey> Types)
{
    public static ExistingTypeIndex Create(ImmutableArray<ExistingTypeKey> declarations, CancellationToken ct)
    {
        var types = new List<ExistingTypeKey>(declarations);

        types.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();
            return ExistingTypeKeyComparer.Instance.Compare(left, right);
        });

        var uniqueTypes = new List<ExistingTypeKey>(types.Count);
        foreach (var type in types)
        {
            ct.ThrowIfCancellationRequested();
            if (uniqueTypes.Count == 0 ||
                ExistingTypeKeyComparer.Instance.Compare(uniqueTypes[uniqueTypes.Count - 1], type) != 0)
            {
                uniqueTypes.Add(type);
            }
        }

        return new ExistingTypeIndex(uniqueTypes.ToEquatableArray());
    }

    public bool Contains(ExistingTypeKey target, CancellationToken ct)
    {
        var low = 0;
        var high = Types.Count - 1;
        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            var comparison = ExistingTypeKeyComparer.Instance.Compare(Types[mid], target);
            if (comparison == 0)
            {
                return true;
            }

            if (comparison < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return false;
    }

    public static ExistingTypeDeclaration? FromDeclaration(SyntaxNode node)
    {
        var key = KeyFromDeclaration(node);
        if (key is null)
        {
            return null;
        }

        return new ExistingTypeDeclaration(
            key.Value,
            DiagnosticLocation.FromLocation(GetNameLocation(node)));
    }

    public static ExistingTypeKey? KeyFromDeclaration(SyntaxNode node)
    {
        if (!TryGetTypeIdentity(node, out var name, out var arity) ||
            !CanCollideWithGeneratedType(name) ||
            IsNestedInType(node) ||
            IsFileLocal(node))
        {
            return null;
        }

        return new ExistingTypeKey(GetNamespace(node), name, arity);
    }

    public static bool IsPotentialGeneratedTypeDeclaration(SyntaxNode node) =>
        TryGetTypeIdentity(node, out var name, out _) && CanCollideWithGeneratedType(name);

    private static bool CanCollideWithGeneratedType(string name) =>
        name.EndsWith("Proxy", System.StringComparison.Ordinal) ||
        name.EndsWith("Dispatcher", System.StringComparison.Ordinal) ||
        name.EndsWith("Async", System.StringComparison.Ordinal) ||
        name == "ShaRpcGeneratedExtensions" ||
        name == "ShaRpcGenerated";

    private static bool TryGetTypeIdentity(SyntaxNode node, out string name, out int arity)
    {
        switch (node)
        {
            case TypeDeclarationSyntax declaration:
                name = declaration.Identifier.ValueText;
                arity = declaration.TypeParameterList?.Parameters.Count ?? 0;
                return true;
            case BaseTypeDeclarationSyntax declaration:
                name = declaration.Identifier.ValueText;
                arity = 0;
                return true;
            case DelegateDeclarationSyntax declaration:
                name = declaration.Identifier.ValueText;
                arity = declaration.TypeParameterList?.Parameters.Count ?? 0;
                return true;
            default:
                name = string.Empty;
                arity = 0;
                return false;
        }
    }

    private static Location? GetNameLocation(SyntaxNode node) =>
        node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier.GetLocation(),
            DelegateDeclarationSyntax declaration => declaration.Identifier.GetLocation(),
            _ => null,
        };

    private static bool IsNestedInType(SyntaxNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFileLocal(SyntaxNode node)
    {
        var modifiers = node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Modifiers,
            DelegateDeclarationSyntax declaration => declaration.Modifiers,
            _ => default,
        };

        foreach (var modifier in modifiers)
        {
            if (modifier.IsKind(SyntaxKind.FileKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var namespaces = new List<string>();
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                namespaces.Add(GetNamespaceName(namespaceDeclaration.Name));
            }
        }

        namespaces.Reverse();
        return string.Join(".", namespaces);
    }

    private static string GetNamespaceName(NameSyntax name) =>
        name switch
        {
            QualifiedNameSyntax qualified => GetNamespaceName(qualified.Left) + "." + GetSimpleName(qualified.Right),
            AliasQualifiedNameSyntax alias => GetSimpleName(alias.Name),
            SimpleNameSyntax simple => GetSimpleName(simple),
            _ => name.ToString().Replace(" ", string.Empty).Replace("@", string.Empty),
        };

    private static string GetSimpleName(SimpleNameSyntax name) =>
        name.Identifier.ValueText;
}

internal readonly record struct ExistingTypeKey(
    string Namespace,
    string Name,
    int Arity);

internal sealed class ExistingTypeKeyComparer : IComparer<ExistingTypeKey>
{
    public static ExistingTypeKeyComparer Instance { get; } = new();

    public int Compare(ExistingTypeKey left, ExistingTypeKey right)
    {
        var ns = string.Compare(left.Namespace, right.Namespace, System.StringComparison.Ordinal);
        if (ns != 0)
        {
            return ns;
        }

        var name = string.Compare(left.Name, right.Name, System.StringComparison.Ordinal);
        return name != 0
            ? name
            : left.Arity.CompareTo(right.Arity);
    }
}
