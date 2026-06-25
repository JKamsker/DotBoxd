using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class RemoteStagedUseDiagnosticFactory
{
    private const string Message =
        "Remote Where/Select stages only lower when the terminal is Run, RunLocal, Register, or RegisterLocal; " +
        "calling Use/UseGeneratedChain after Where/Select would ignore those stages.";
    private const string DiscardedStageMessage =
        "Remote Where/Select stages must be chained into Run, RunLocal, Register, or RegisterLocal; " +
        "discarding the stage result would ignore the stage.";

    public static bool IsCandidate(SyntaxNode node)
        => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Use"
                    or "UseGeneratedChain"
                    or "UseGeneratedLocalChain"
                    or "UseGeneratedResultChain"
                    or "UseGeneratedLocalResultChain"
                    or "Where"
                    or "Select"
            }
        };

    public static PluginKernelDiagnostic? Create(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return null;
        }

        if (access.Name.Identifier.ValueText is "Where" or "Select")
        {
            return CreateDiscardedStageDiagnostic(invocation, access, context.SemanticModel, cancellationToken);
        }

        var receiverType = context.SemanticModel.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (!ContainsStageInvocationOrAlias(access.Expression, context.SemanticModel, cancellationToken) &&
            !IsRemoteStageType(receiverType))
        {
            return null;
        }

        if (!IsRemoteChainOrStageType(receiverType) &&
            !IsGeneratedRemoteChain(access.Expression, context.SemanticModel, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            Message,
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
    }

    private static PluginKernelDiagnostic? CreateDiscardedStageDiagnostic(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Parent is not ExpressionStatementSyntax)
        {
            return null;
        }

        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (!IsRemoteChainOrStageType(receiverType) &&
            !IsGeneratedRemoteChain(access.Expression, model, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            DiscardedStageMessage,
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
    }

    private static bool ContainsStageInvocation(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax stageAccess
            })
        {
            var stageName = stageAccess.Name.Identifier.ValueText;
            return stageName is "Where" or "Select" ||
                ContainsStageInvocation(stageAccess.Expression);
        }

        return expression is MemberAccessExpressionSyntax access &&
            ContainsStageInvocation(access.Expression);
    }

    private static bool ContainsStageInvocationOrAlias(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (ContainsStageInvocation(expression))
        {
            return true;
        }

        return HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer &&
            ContainsStageInvocation(initializer);
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
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        if (HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer)
        {
            return WalkToSeed(initializer, model, cancellationToken);
        }

        var current = expression;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax access)
        {
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

        return null;
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
                "RemoteSubscriptionPipeline",
            "DotBoxD.Plugins.Runtime.Hooks" => name == "RemoteHookStage",
            "DotBoxD.Plugins.Runtime.Subscriptions" => name == "RemoteSubscriptionStage",
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
            "DotBoxD.Plugins.Runtime.Hooks" => named.Name == "RemoteHookStage",
            "DotBoxD.Plugins.Runtime.Subscriptions" => named.Name == "RemoteSubscriptionStage",
            _ => false
        };
    }
}
