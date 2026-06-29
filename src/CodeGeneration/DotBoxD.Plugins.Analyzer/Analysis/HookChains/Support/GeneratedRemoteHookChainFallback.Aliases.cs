using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static GeneratedRemoteHookChainTarget? TargetFromLocalAlias(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (expression is not IdentifierNameSyntax identifier ||
            model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return null;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax(cancellationToken))
            {
                case VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer
                }:
                    return RegistryTarget(initializer, model, cancellationToken, depth + 1);
                case SingleVariableDesignationSyntax designation
                    when DeconstructionInitializer(designation, cancellationToken) is { } initializer:
                    return RegistryTarget(initializer, model, cancellationToken, depth + 1);
            }
        }

        return null;
    }

    private static ExpressionSyntax? DeconstructionInitializer(
        SingleVariableDesignationSyntax designation,
        CancellationToken cancellationToken)
    {
        if (!TryDeconstructionPath(designation, out var declaration, out var path) ||
            declaration is null ||
            declaration.Parent is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return null;
        }

        var current = assignment.Right;
        for (var indexIndex = path.Count - 1; indexIndex >= 0; indexIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = HookChainAliasResolver.UnwrapTransparentExpression(current);
            var index = path[indexIndex];
            if (current is not TupleExpressionSyntax tuple ||
                index >= tuple.Arguments.Count)
            {
                return null;
            }

            current = tuple.Arguments[index].Expression;
        }

        return current;
    }

    private static bool TryDeconstructionPath(
        SingleVariableDesignationSyntax designation,
        out DeclarationExpressionSyntax? declaration,
        out List<int> path)
    {
        path = new List<int>();
        declaration = null;
        VariableDesignationSyntax current = designation;
        while (current.Parent is ParenthesizedVariableDesignationSyntax variables)
        {
            var index = variables.Variables.IndexOf(current);
            if (index < 0)
            {
                return false;
            }

            path.Add(index);
            switch (variables.Parent)
            {
                case DeclarationExpressionSyntax root:
                    declaration = root;
                    return true;
                case ParenthesizedVariableDesignationSyntax:
                    current = variables;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }
}
