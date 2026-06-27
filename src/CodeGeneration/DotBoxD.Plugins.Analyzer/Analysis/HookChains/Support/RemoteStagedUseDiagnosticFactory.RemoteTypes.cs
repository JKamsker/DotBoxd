using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private static bool ContainsStageInvocationOrAlias(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => ContainsStageInvocationOrAlias(expression, model, cancellationToken, depth: 0);

    private static bool ContainsStageInvocationOrAlias(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        if (ContainsStageInvocation(expression))
        {
            return true;
        }

        if (ReturnedExpression(expression, model, cancellationToken) is { } returned)
        {
            return ContainsStageInvocationOrAlias(returned, model, cancellationToken, depth + 1);
        }

        return HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer &&
            ContainsStageInvocationOrAlias(initializer, model, cancellationToken, depth + 1);
    }

    private static ExpressionSyntax? ReturnedExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.ExpressionBody is { Expression: { } expressionBody })
            {
                return expressionBody;
            }

            if (declaration.Body is { } body)
            {
                var returns = body.DescendantNodes().OfType<ReturnStatementSyntax>().ToArray();
                if (returns.Length == 1)
                {
                    return returns[0].Expression;
                }
            }
        }

        return null;
    }

    private static bool IsGeneratedRemoteChain(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var seed = WalkToSeed(expression, model, cancellationToken);
        return seed is not null &&
            GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken) is not null;
    }

    private static InvocationExpressionSyntax? WalkToSeed(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);

        if (HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer)
        {
            return WalkToSeed(initializer, model, cancellationToken);
        }

        var current = expression;
        while (true)
        {
            current = HookChainAliasResolver.UnwrapTransparentExpression(current);
            if (HookChainAliasResolver.Initializer(current, model, cancellationToken) is { } currentInitializer)
            {
                current = currentInitializer;
                continue;
            }

            if (current is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax access)
            {
                return null;
            }

            var name = access.Name.Identifier.ValueText;
            if (string.Equals(name, "On", StringComparison.Ordinal))
            {
                return invocation;
            }

            if (name is "Where" or "Select")
            {
                current = access.Expression;
                continue;
            }

            return null;
        }
    }

    private static bool IsRemoteChainOrStageType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var name = named.Name;
        var ns = named.ContainingNamespace.ToDisplayString();
        return ns switch
        {
            "DotBoxD.Plugins.Runtime" => name is
                "RemoteHookPipeline" or
                "RemoteHookPipelineWithContext" or
                "RemoteSubscriptionPipeline" or
                "RemoteSubscriptionPipelineWithContext",
            "DotBoxD.Plugins.Runtime.Hooks" => name is "RemoteHookStage" or "RemoteHookStageWithContext",
            "DotBoxD.Plugins.Runtime.Subscriptions" => name is
                "RemoteSubscriptionStage" or
                "RemoteSubscriptionStageWithContext",
            _ => false
        };
    }

    private static bool IsRemoteStageType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var ns = named.ContainingNamespace.ToDisplayString();
        return ns switch
        {
            "DotBoxD.Plugins.Runtime.Hooks" => named.Name is "RemoteHookStage" or "RemoteHookStageWithContext",
            "DotBoxD.Plugins.Runtime.Subscriptions" => named.Name is
                "RemoteSubscriptionStage" or
                "RemoteSubscriptionStageWithContext",
            _ => false
        };
    }
}
