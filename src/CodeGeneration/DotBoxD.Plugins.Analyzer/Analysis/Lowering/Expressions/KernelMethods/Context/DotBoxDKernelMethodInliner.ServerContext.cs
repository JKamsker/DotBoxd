using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static IMethodSymbol? ResolveKernelMethodInvocation(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)
        {
            return method;
        }

        return TryResolveServerContextKernelMethod(invocation, context);
    }

    private static IMethodSymbol? TryResolveServerContextKernelMethod(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.ServerContextType is not INamedTypeSymbol contextType ||
            invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax receiver,
                Name: SimpleNameSyntax methodName
            } ||
            !string.Equals(receiver.Identifier.ValueText, context.ServerContextParameterName, StringComparison.Ordinal))
        {
            return null;
        }

        var matches = new List<IMethodSymbol>();
        foreach (var candidate in contextType.GetMembers(methodName.Identifier.ValueText).OfType<IMethodSymbol>())
        {
            if (candidate.IsStatic ||
                !HasKernelMethodAttribute(candidate, context.SemanticModel.Compilation) ||
                !CanBind(invocation, candidate))
            {
                continue;
            }

            matches.Add(candidate);
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new NotSupportedException(
                $"[KernelMethod] context call '{methodName.Identifier.ValueText}' is ambiguous before generated APIs bind.")
        };
    }

    private static bool CanBind(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        try
        {
            _ = KernelMethodArgumentBinder.Bind(invocation, method, $"[KernelMethod] '{method.Name}'");
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsServerContextReceiver(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.ServerContextType is null ||
            !IsServerContextMethod(method, context.ServerContextType))
        {
            return false;
        }

        return invocation.Expression switch
        {
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } => true,
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } =>
                string.Equals(receiver.Identifier.ValueText, context.ServerContextParameterName, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool IsServerContextMethod(IMethodSymbol method, ITypeSymbol serverContextType)
    {
        for (var current = serverContextType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(method.ContainingType, current))
            {
                return true;
            }
        }

        return false;
    }
}
