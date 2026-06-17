using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// Stable identity for a lowered hook chain: a deterministic id from the chain's source file + span
/// (no clock/random, so generator incrementality is preserved) and the enclosing namespace, so each
/// chain emits a distinct package without colliding with kernel packages.
/// </summary>
internal static class HookChainIdentity
{
    public static string Compute(InvocationExpressionSyntax invocation)
    {
        var span = invocation.GetLocation().GetLineSpan();
        var seed = span.Path + ":" +
                   invocation.SpanStart.ToString(CultureInfo.InvariantCulture);
        return Fnv1a(seed).ToString("x16", CultureInfo.InvariantCulture);
    }

    public static string Namespace(SyntaxNode node)
    {
        var namespaces = new Stack<string>();
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case FileScopedNamespaceDeclarationSyntax fileScoped:
                    namespaces.Push(fileScoped.Name.ToString());
                    break;
                case NamespaceDeclarationSyntax declared:
                    namespaces.Push(declared.Name.ToString());
                    break;
            }
        }

        return string.Join(".", namespaces);
    }

    private static ulong Fnv1a(string text)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}
