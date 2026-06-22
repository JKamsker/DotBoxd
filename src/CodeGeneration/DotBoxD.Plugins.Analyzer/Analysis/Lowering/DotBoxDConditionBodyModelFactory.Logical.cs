using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static partial class DotBoxDConditionBodyModelFactory
{
    private static DotBoxDStatementBodyModel LowerAnd(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        var operands = FlattenAnd(binary);
        var contexts = new DotBoxDExpressionLoweringContext[operands.Count];
        var runningContext = context;
        for (var i = 0; i < operands.Count; i++)
        {
            contexts[i] = runningContext;
            if (DotBoxDPatternExpressionLowerer.TryLowerDeclarationPattern(
                    operands[i],
                    runningContext,
                    part => DotBoxDExpressionModelFactory.Create(part, runningContext),
                    out _,
                    out var captureName,
                    out var capture))
            {
                runningContext = runningContext.WithPatternCapture(captureName, capture);
            }
        }

        var next = whenTrue;
        for (var i = operands.Count - 1; i >= 0; i--)
        {
            next = LowerCondition(operands[i], next, whenFalse, contexts[i]);
        }

        return next;
    }

    private static DotBoxDStatementBodyModel LowerOr(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        var whenLeftFalse = LowerCondition(binary.Right, whenTrue, whenFalse, context);
        return LowerCondition(binary.Left, whenTrue, whenLeftFalse, context);
    }

    private static IReadOnlyList<ExpressionSyntax> FlattenAnd(ExpressionSyntax expression)
    {
        var operands = new List<ExpressionSyntax>();
        AddAndOperand(expression, operands);
        return operands;
    }

    private static void AddAndOperand(ExpressionSyntax expression, ICollection<ExpressionSyntax> operands)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            AddAndOperand(parenthesized.Expression, operands);
            return;
        }

        if (expression is BinaryExpressionSyntax binary && binary.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression)
        {
            AddAndOperand(binary.Left, operands);
            AddAndOperand(binary.Right, operands);
            return;
        }

        operands.Add(expression);
    }
}
