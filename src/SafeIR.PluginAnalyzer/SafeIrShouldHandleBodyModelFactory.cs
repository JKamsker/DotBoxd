namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrShouldHandleBodyModelFactory
{
    public static SafeIrStatementBodyModel Create(
        MethodDeclarationSyntax method,
        SafeIrExpressionLoweringContext context)
    {
        if (method.ExpressionBody?.Expression is { } expression)
        {
            return SafeIrConditionBodyModelFactory.Create(expression, context);
        }

        if (method.Body is null || method.Body.Statements.Count != 1)
        {
            throw new NotSupportedException(
                "Kernel ShouldHandle must be an expression body, a single return, or a single if/else return statement.");
        }

        return LowerStatement(method.Body.Statements[0], context);
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
            _ => throw new NotSupportedException(
                "Kernel ShouldHandle block bodies may only contain return statements or if/else return statements.")
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
