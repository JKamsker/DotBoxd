using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private const string Message =
        "Remote Where/Select stages only lower when the terminal is Run, RunLocal, Register, or RegisterLocal; " +
        "calling Use/UseGeneratedChain after Where/Select would ignore those stages.";
    private const string DiscardedStageMessage =
        "Remote Where/Select stages must be chained into Run, RunLocal, Register, or RegisterLocal; " +
        "discarding the stage result would ignore the stage.";
    private const string AssignedStageMessage =
        "Remote Where/Select stages assigned to an existing local are not lowered into a later terminal; " +
        "keep the stage in the fluent chain or initialize a new local with the staged expression.";

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
        var transparentExpression = UnwrapTransparentParent(invocation);
        if (transparentExpression.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Right == transparentExpression &&
            assignment.Parent is ExpressionStatementSyntax)
        {
            return CreateAssignedStageDiagnostic(access, model, cancellationToken);
        }

        if (transparentExpression.Parent is EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax declarator
            } &&
            model.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol local)
        {
            return CreateStagedLocalDiagnostic(invocation, access, model, local, cancellationToken);
        }

        if (transparentExpression.Parent is not ExpressionStatementSyntax)
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

    private static ExpressionSyntax UnwrapTransparentParent(ExpressionSyntax expression)
    {
        while (expression.Parent is ParenthesizedExpressionSyntax parenthesized &&
               parenthesized.Expression == expression ||
               expression.Parent is PostfixUnaryExpressionSyntax postfix &&
               postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
               postfix.Operand == expression)
        {
            expression = (ExpressionSyntax)expression.Parent;
        }

        return expression;
    }

    private static PluginKernelDiagnostic? CreateStagedLocalDiagnostic(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        ILocalSymbol local,
        CancellationToken cancellationToken)
    {
        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if ((!IsRemoteChainOrStageType(receiverType) &&
             !IsGeneratedRemoteChain(access.Expression, model, cancellationToken)) ||
            LocalFlowsIntoTerminalOrUse(invocation, local, model, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            DiscardedStageMessage,
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
    }

    private static PluginKernelDiagnostic? CreateAssignedStageDiagnostic(
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (!IsRemoteChainOrStageType(receiverType) &&
            !IsGeneratedRemoteChain(access.Expression, model, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            AssignedStageMessage,
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
    }

    private static bool LocalFlowsIntoTerminalOrUse(
        InvocationExpressionSyntax invocation,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var block = invocation.FirstAncestorOrSelf<BlockSyntax>();
        if (block is null)
        {
            return false;
        }

        foreach (var terminal in block.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (terminal == invocation ||
                terminal.SpanStart <= invocation.SpanStart ||
                terminal.Expression is not MemberAccessExpressionSyntax access)
            {
                continue;
            }

            if (access.Name.Identifier.ValueText is not
                ("Run" or "RunLocal" or "Register" or "RegisterLocal" or
                 "Use" or "UseGeneratedChain" or "UseGeneratedLocalChain" or
                 "UseGeneratedResultChain" or "UseGeneratedLocalResultChain"))
            {
                continue;
            }

            if (IsAssignedBetween(local, invocation.SpanStart, terminal.SpanStart, model, cancellationToken))
            {
                continue;
            }

            if (ExpressionReferencesLocal(access.Expression, local, model, cancellationToken, depth: 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAssignedBetween(
        ILocalSymbol local,
        int start,
        int end,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var block = local.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken).FirstAncestorOrSelf<BlockSyntax>())
            .FirstOrDefault(candidate => candidate is not null);
        if (block is null)
        {
            return false;
        }

        foreach (var assignment in block.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assignment.SpanStart <= start ||
                assignment.SpanStart >= end ||
                model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not ILocalSymbol assigned ||
                !SymbolEqualityComparer.Default.Equals(local, assigned))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool ExpressionReferencesLocal(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        if (expression is IdentifierNameSyntax identifier &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
        {
            return true;
        }

        if (HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer &&
            ExpressionReferencesLocal(initializer, local, model, cancellationToken, depth + 1))
        {
            return true;
        }

        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax chained
            })
        {
            return ExpressionReferencesLocal(chained.Expression, local, model, cancellationToken, depth + 1);
        }

        return expression is MemberAccessExpressionSyntax access &&
            ExpressionReferencesLocal(access.Expression, local, model, cancellationToken, depth + 1);
    }

    private static bool ContainsStageInvocation(ExpressionSyntax expression)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);

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

}
