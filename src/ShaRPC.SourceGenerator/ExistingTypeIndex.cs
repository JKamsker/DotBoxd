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

        return new ExistingTypeIndex(types.ToEquatableArray());
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
        if (!TryGetTypeName(node, out var name) || IsNestedInType(node) || IsFileLocal(node))
        {
            return null;
        }

        if (!CanCollideWithGeneratedType(name))
        {
            return null;
        }

        return new ExistingTypeKey(GetNamespace(node), name);
    }

    private static bool CanCollideWithGeneratedType(string name) =>
        name.EndsWith("Proxy", System.StringComparison.Ordinal) ||
        name.EndsWith("Dispatcher", System.StringComparison.Ordinal) ||
        name.EndsWith("Async", System.StringComparison.Ordinal) ||
        name == "ShaRpcGeneratedExtensions";

    private static bool TryGetTypeName(SyntaxNode node, out string name)
    {
        switch (node)
        {
            case BaseTypeDeclarationSyntax declaration:
                name = declaration.Identifier.ValueText;
                return true;
            case DelegateDeclarationSyntax declaration:
                name = declaration.Identifier.ValueText;
                return true;
            default:
                name = string.Empty;
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
                namespaces.Add(namespaceDeclaration.Name.ToString().Replace("@", string.Empty));
            }
        }

        namespaces.Reverse();
        return string.Join(".", namespaces);
    }
}

internal sealed record ExistingTypeLocationIndex(EquatableArray<ExistingTypeDeclaration> Types)
{
    public static ExistingTypeLocationIndex Create(
        ImmutableArray<ExistingTypeDeclaration> declarations,
        CancellationToken ct)
    {
        var ordered = new List<ExistingTypeDeclaration>(declarations);
        ordered.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();

            var key = ExistingTypeKeyComparer.Instance.Compare(left.Key, right.Key);
            return key != 0
                ? key
                : left.Location.Start.CompareTo(right.Location.Start);
        });

        return new ExistingTypeLocationIndex(ordered.ToEquatableArray());
    }

    public DiagnosticLocation Find(ExistingTypeKey target, CancellationToken ct)
    {
        var low = 0;
        var high = Types.Count - 1;
        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            var comparison = ExistingTypeKeyComparer.Instance.Compare(Types[mid].Key, target);
            if (comparison == 0)
            {
                return Types[mid].Location;
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

        return default;
    }
}

internal readonly record struct ExistingTypeDeclaration(
    ExistingTypeKey Key,
    DiagnosticLocation Location);

internal readonly record struct ExistingTypeKey(
    string Namespace,
    string Name);

internal sealed class ExistingTypeKeyComparer : IComparer<ExistingTypeKey>
{
    public static ExistingTypeKeyComparer Instance { get; } = new();

    public int Compare(ExistingTypeKey left, ExistingTypeKey right)
    {
        var ns = string.Compare(left.Namespace, right.Namespace, System.StringComparison.Ordinal);
        return ns != 0
            ? ns
            : string.Compare(left.Name, right.Name, System.StringComparison.Ordinal);
    }
}
