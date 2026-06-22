using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDShouldHandleBodyModelFactory
{
    private const string UnsupportedShapeMessage =
        "Kernel ShouldHandle must be an expression body, a single return, an if/else return, or a guard if followed by a return.";

    public static DotBoxDStatementBodyModel Create(
        MethodDeclarationSyntax method,
        DotBoxDExpressionLoweringContext context)
    {
        if (method.ExpressionBody?.Expression is { } expression)
        {
            return DotBoxDConditionBodyModelFactory.Create(expression, context);
        }

        if (method.Body is null)
        {
            throw new NotSupportedException(UnsupportedShapeMessage);
        }

        return LowerBlock(method.Body, context);
    }

    private static DotBoxDStatementBodyModel LowerBlock(
        BlockSyntax block,
        DotBoxDExpressionLoweringContext context)
        => LowerStatements(block.Statements, start: 0, context);

    private static DotBoxDStatementBodyModel LowerStatements(
        SyntaxList<StatementSyntax> statements,
        int start,
        DotBoxDExpressionLoweringContext context)
    {
        var remaining = statements.Count - start;
        if (remaining == 1)
        {
            return LowerStatement(statements[start], context);
        }

        if (remaining > 1 &&
            statements[start] is IfStatementSyntax branch &&
            branch.Else is null)
        {
            RejectDeclarationPatternBranch(branch);
            var whenTrue = LowerStatement(branch.Statement, context);
            var whenFalse = LowerStatements(statements, start + 1, context);
            return DotBoxDConditionBodyModelFactory.CreateBranch(
                branch.Condition,
                whenTrue,
                whenFalse,
                context);
        }

        throw new NotSupportedException(UnsupportedShapeMessage);
    }

    private static DotBoxDStatementBodyModel LowerStatement(
        StatementSyntax statement,
        DotBoxDExpressionLoweringContext context)
        => statement switch
        {
            ReturnStatementSyntax ret when ret.Expression is not null =>
                DotBoxDConditionBodyModelFactory.Create(ret.Expression, context),
            IfStatementSyntax branch when branch.Else is not null =>
                LowerIf(branch, context),
            BlockSyntax block when block.Statements.Count == 1 =>
                LowerStatement(block.Statements[0], context),
            _ => throw new NotSupportedException(UnsupportedShapeMessage)
        };

    private static DotBoxDStatementBodyModel LowerIf(
        IfStatementSyntax branch,
        DotBoxDExpressionLoweringContext context)
    {
        RejectDeclarationPatternBranch(branch);
        var whenTrue = LowerStatement(branch.Statement, context);
        var whenFalse = LowerStatement(branch.Else!.Statement, context);
        return DotBoxDConditionBodyModelFactory.CreateBranch(
            branch.Condition,
            whenTrue,
            whenFalse,
            context);
    }

    private static void RejectDeclarationPatternBranch(IfStatementSyntax branch)
    {
        if (DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(branch.Condition))
        {
            throw new NotSupportedException($"Unsupported declaration-pattern composition '{branch.Condition}'.");
        }
    }
}
