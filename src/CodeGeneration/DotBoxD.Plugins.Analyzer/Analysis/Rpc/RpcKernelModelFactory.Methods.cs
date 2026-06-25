using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class RpcKernelModelFactory
{
    private static IMethodSymbol ResolveBatchMethod(INamedTypeSymbol type, Compilation compilation)
    {
        var count = 0;
        IMethodSymbol? found = null;
        foreach (var method in BatchMethods(type, compilation))
        {
            count++;
            if (count > 1)
            {
                throw new NotSupportedException("A server extension must declare exactly one batch method (a public method whose last parameter is HookContext or a generated plugin context).");
            }

            found = method;
        }

        return found ?? throw new NotSupportedException("A server extension must declare one public batch method whose last parameter is HookContext or a generated plugin context.");
    }

    internal static IMethodSymbol? TryResolveBatchMethod(INamedTypeSymbol type, Compilation compilation)
    {
        IMethodSymbol? found = null;
        foreach (var method in BatchMethods(type, compilation))
        {
            if (found is not null)
            {
                return null;
            }

            found = method;
        }

        return found;
    }

    private static IEnumerable<IMethodSymbol> BatchMethods(INamedTypeSymbol type, Compilation compilation)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    DeclaredAccessibility: Accessibility.Public,
                    IsStatic: false
                } method &&
                method.Parameters.Length > 0 &&
                RpcKernelContextParameter.IsSupported(method.Parameters[method.Parameters.Length - 1], compilation))
            {
                yield return method;
            }
        }
    }

    private static void ValidateBatchMethodParameters(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"Server extension parameter '{parameter.Name}' cannot use ref, in, or out modifiers.");
            }
        }
    }

    private static BlockSyntax MethodBody(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax { Body: { } block })
            {
                return block;
            }
        }

        throw new NotSupportedException($"Server extension method '{method.Name}' must have a block body declared in source.");
    }
}
