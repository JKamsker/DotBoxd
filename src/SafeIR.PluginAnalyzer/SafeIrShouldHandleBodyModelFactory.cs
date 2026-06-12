namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrShouldHandleBodyModelFactory
{
    private const string UnsupportedShapeMessage =
        "Kernel ShouldHandle must be an expression body, a single return, an if/else return, or a guard if followed by a return.";

    public static SafeIrStatementBodyModel Create(
        MethodDeclarationSyntax method,
        SafeIrExpressionLoweringContext context)
    {
        if (method.ExpressionBody?.Expression is { } expression)
        {
            return SafeIrConditionBodyModelFactory.Create(expression, context);
        }

        if (method.Body is null)
        {
            throw new NotSupportedException(UnsupportedShapeMessage);
        }

        return LowerBlock(method.Body, context);
    }

    private static SafeIrStatementBodyModel LowerBlock(
        BlockSyntax block,
        SafeIrExpressionLoweringContext context)
        => LowerStatements(block.Statements, start: 0, context);

    private static SafeIrStatementBodyModel LowerStatements(
        SyntaxList<StatementSyntax> statements,
        int start,
        SafeIrExpressionLoweringContext context)
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
            return SafeIrConditionBodyModelFactory.CreateBranch(
                branch.Condition,
                whenTrue,
                whenFalse,
                context);
        }

        throw new NotSupportedException(UnsupportedShapeMessage);
    }

    private static SafeIrStatementBodyModel LowerStatement(
        StatementSyntax statement,
        SafeIrExpressionLoweringContext context)
        => statement switch {
            ReturnStatementSyntax ret when ret.Expression is not null =>
                SafeIrConditionBodyModelFactory.Create(ret.Expression, context),
            IfStatementSyntax branch when branch.Else is not null =>
                LowerIf(branch, context),
            BlockSyntax block when block.Statements.Count == 1 =>
                LowerStatement(block.Statements[0], context),
            _ => throw new NotSupportedException(UnsupportedShapeMessage)
        };

    private static SafeIrStatementBodyModel LowerIf(
        IfStatementSyntax branch,
        SafeIrExpressionLoweringContext context)
    {
        var whenTrue = LowerStatement(branch.Statement, context);
        var whenFalse = LowerStatement(branch.Else!.Statement, context);
        return SafeIrConditionBodyModelFactory.CreateBranch(
            branch.Condition,
            whenTrue,
            whenFalse,
            context);
    }
}
