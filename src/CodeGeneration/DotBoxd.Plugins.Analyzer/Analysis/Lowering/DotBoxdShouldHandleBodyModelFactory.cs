namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdShouldHandleBodyModelFactory
{
    private const string UnsupportedShapeMessage =
        "Kernel ShouldHandle must be an expression body, a single return, an if/else return, or a guard if followed by a return.";

    public static DotBoxdStatementBodyModel Create(
        MethodDeclarationSyntax method,
        DotBoxdExpressionLoweringContext context)
    {
        if (method.ExpressionBody?.Expression is { } expression)
        {
            return DotBoxdConditionBodyModelFactory.Create(expression, context);
        }

        if (method.Body is null)
        {
            throw new NotSupportedException(UnsupportedShapeMessage);
        }

        return LowerBlock(method.Body, context);
    }

    private static DotBoxdStatementBodyModel LowerBlock(
        BlockSyntax block,
        DotBoxdExpressionLoweringContext context)
        => LowerStatements(block.Statements, start: 0, context);

    private static DotBoxdStatementBodyModel LowerStatements(
        SyntaxList<StatementSyntax> statements,
        int start,
        DotBoxdExpressionLoweringContext context)
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
            var whenTrue = LowerStatement(branch.Statement, context);
            var whenFalse = LowerStatements(statements, start + 1, context);
            return DotBoxdConditionBodyModelFactory.CreateBranch(
                branch.Condition,
                whenTrue,
                whenFalse,
                context);
        }

        throw new NotSupportedException(UnsupportedShapeMessage);
    }

    private static DotBoxdStatementBodyModel LowerStatement(
        StatementSyntax statement,
        DotBoxdExpressionLoweringContext context)
        => statement switch {
            ReturnStatementSyntax ret when ret.Expression is not null =>
                DotBoxdConditionBodyModelFactory.Create(ret.Expression, context),
            IfStatementSyntax branch when branch.Else is not null =>
                LowerIf(branch, context),
            BlockSyntax block when block.Statements.Count == 1 =>
                LowerStatement(block.Statements[0], context),
            _ => throw new NotSupportedException(UnsupportedShapeMessage)
        };

    private static DotBoxdStatementBodyModel LowerIf(
        IfStatementSyntax branch,
        DotBoxdExpressionLoweringContext context)
    {
        var whenTrue = LowerStatement(branch.Statement, context);
        var whenFalse = LowerStatement(branch.Else!.Statement, context);
        return DotBoxdConditionBodyModelFactory.CreateBranch(
            branch.Condition,
            whenTrue,
            whenFalse,
            context);
    }
}
