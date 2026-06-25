using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class RemoteStagedUseDiagnosticFactory
{
    private const string Message =
        "Remote Where/Select stages only lower when the terminal is Run, RunLocal, Register, or RegisterLocal; " +
        "calling Use/UseGeneratedChain after Where/Select would ignore those stages.";

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
            }
        };

    public static PluginKernelDiagnostic? Create(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax access ||
            !ContainsStageInvocation(access.Expression))
        {
            return null;
        }

        var receiverType = context.SemanticModel.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (!IsRemoteChainType(receiverType) &&
            !IsGeneratedRemoteChain(access.Expression, context.SemanticModel, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            Message,
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

    private static bool IsGeneratedRemoteChain(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var seed = WalkToSeed(expression);
        return seed is not null &&
            GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken) is not null;
    }

    private static InvocationExpressionSyntax? WalkToSeed(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
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

    private static bool IsRemoteChainType(ITypeSymbol? type)
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
}
