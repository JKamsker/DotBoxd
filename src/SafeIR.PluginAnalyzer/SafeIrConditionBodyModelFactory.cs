namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrConditionBodyModelFactory
{
    public static SafeIrStatementBodyModel Create(
        ExpressionSyntax expression,
        SafeIrExpressionLoweringContext context)
        => LowerCondition(expression, ReturnBool(value: true), ReturnBool(value: false), context);

    private static SafeIrStatementBodyModel LowerCondition(
        ExpressionSyntax expression,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        return expression switch {
            ParenthesizedExpressionSyntax parenthesized =>
                LowerCondition(parenthesized.Expression, whenTrue, whenFalse, context),
            ConditionalExpressionSyntax conditional =>
                LowerConditional(conditional, whenTrue, whenFalse, context),
            PrefixUnaryExpressionSyntax unary when unary.Kind() == SyntaxKind.LogicalNotExpression =>
                LowerCondition(unary.Operand, whenFalse, whenTrue, context),
            BinaryExpressionSyntax binary when binary.Kind() == SyntaxKind.LogicalAndExpression =>
                LowerAnd(binary, whenTrue, whenFalse, context),
            BinaryExpressionSyntax binary when binary.Kind() == SyntaxKind.LogicalOrExpression =>
                LowerOr(binary, whenTrue, whenFalse, context),
            BinaryExpressionSyntax binary when IsEagerAnd(binary, context) =>
                LowerEagerAnd(binary, whenTrue, whenFalse, context),
            BinaryExpressionSyntax binary when IsEagerOr(binary, context) =>
                LowerEagerOr(binary, whenTrue, whenFalse, context),
            BinaryExpressionSyntax binary when IsBoolXor(binary, context) =>
                LowerBoolXor(binary, whenTrue, whenFalse, context),
            BinaryExpressionSyntax binary when IsBoolEquality(binary, context) =>
                LowerBoolEquality(binary, whenTrue, whenFalse, context),
            _ => LowerLeafCondition(expression, whenTrue, whenFalse, context)
        };
    }

    private static SafeIrStatementBodyModel LowerAnd(
        BinaryExpressionSyntax binary,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
        => LowerCondition(
            binary.Left,
            LowerCondition(binary.Right, whenTrue, whenFalse, context),
            whenFalse,
            context);

    private static SafeIrStatementBodyModel LowerOr(
        BinaryExpressionSyntax binary,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
        => LowerCondition(
            binary.Left,
            whenTrue,
            LowerCondition(binary.Right, whenTrue, whenFalse, context),
            context);

    private static SafeIrStatementBodyModel LowerConditional(
        ConditionalExpressionSyntax conditional,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
        => LowerCondition(
            conditional.Condition,
            LowerCondition(conditional.WhenTrue, whenTrue, whenFalse, context),
            LowerCondition(conditional.WhenFalse, whenTrue, whenFalse, context),
            context);

    private static SafeIrStatementBodyModel LowerEagerAnd(
        BinaryExpressionSyntax binary,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
        => LowerCondition(
            binary.Left,
            LowerCondition(binary.Right, whenTrue, whenFalse, context),
            LowerCondition(binary.Right, whenFalse, whenFalse, context),
            context);

    private static SafeIrStatementBodyModel LowerEagerOr(
        BinaryExpressionSyntax binary,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
        => LowerCondition(
            binary.Left,
            LowerCondition(binary.Right, whenTrue, whenTrue, context),
            LowerCondition(binary.Right, whenTrue, whenFalse, context),
            context);

    private static SafeIrStatementBodyModel LowerBoolXor(
        BinaryExpressionSyntax binary,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
        => LowerCondition(
            binary.Left,
            LowerCondition(binary.Right, whenFalse, whenTrue, context),
            LowerCondition(binary.Right, whenTrue, whenFalse, context),
            context);

    private static SafeIrStatementBodyModel LowerBoolEquality(
        BinaryExpressionSyntax binary,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
    {
        var rightMatchesTrue = LowerCondition(binary.Right, whenTrue, whenFalse, context);
        var rightMatchesFalse = LowerCondition(binary.Right, whenFalse, whenTrue, context);
        if (binary.Kind() == SyntaxKind.NotEqualsExpression)
        {
            (rightMatchesTrue, rightMatchesFalse) = (rightMatchesFalse, rightMatchesTrue);
        }

        return LowerCondition(binary.Left, rightMatchesTrue, rightMatchesFalse, context);
    }

    private static SafeIrStatementBodyModel LowerLeafCondition(
        ExpressionSyntax expression,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        SafeIrExpressionLoweringContext context)
    {
        if (ContainsOrderedBooleanOperator(expression))
        {
            throw new NotSupportedException(
                $"Unsupported ordered bool expression inside '{expression}'.");
        }

        var condition = SafeIrExpressionModelFactory.Create(expression, context);
        RequireBool(condition, expression);
        return If(condition.Source, whenTrue, whenFalse, condition.Allocates);
    }

    private static SafeIrStatementBodyModel If(
        string condition,
        SafeIrStatementBodyModel whenTrue,
        SafeIrStatementBodyModel whenFalse,
        bool conditionAllocates)
    {
        var source =
            $"[new {SafeIrGenerationNames.IrTypes.IfStatement}({condition}, {whenTrue.Source}, {whenFalse.Source}, Span)]";
        return new SafeIrStatementBodyModel(
            source,
            conditionAllocates || whenTrue.Allocates || whenFalse.Allocates);
    }

    private static SafeIrStatementBodyModel ReturnBool(bool value)
        => ReturnExpression(
            $"{SafeIrGenerationNames.Helpers.Bool}({BoolLiteral(value)})",
            allocates: false);

    private static SafeIrStatementBodyModel ReturnExpression(string expression, bool allocates)
        => new($"[new {SafeIrGenerationNames.IrTypes.ReturnStatement}({expression}, Span)]", allocates);

    private static string BoolLiteral(bool value)
        => value
            ? SafeIrGenerationNames.CSharpLiterals.True
            : SafeIrGenerationNames.CSharpLiterals.False;

    private static bool IsBoolEquality(
        BinaryExpressionSyntax binary,
        SafeIrExpressionLoweringContext context)
    {
        if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
        {
            return false;
        }

        return IsBool(binary.Left, context) && IsBool(binary.Right, context);
    }

    private static bool IsEagerAnd(
        BinaryExpressionSyntax binary,
        SafeIrExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.BitwiseAndExpression && IsBoolOperands(binary, context);

    private static bool IsEagerOr(
        BinaryExpressionSyntax binary,
        SafeIrExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.BitwiseOrExpression && IsBoolOperands(binary, context);

    private static bool IsBoolXor(
        BinaryExpressionSyntax binary,
        SafeIrExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.ExclusiveOrExpression && IsBoolOperands(binary, context);

    private static bool IsBoolOperands(
        BinaryExpressionSyntax binary,
        SafeIrExpressionLoweringContext context)
        => IsBool(binary.Left, context) && IsBool(binary.Right, context);

    private static bool IsBool(ExpressionSyntax expression, SafeIrExpressionLoweringContext context)
        => context.SemanticModel
            .GetTypeInfo(expression, context.CancellationToken)
            .ConvertedType?.SpecialType == SpecialType.System_Boolean;

    private static bool ContainsOrderedBooleanOperator(ExpressionSyntax expression)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is BinaryExpressionSyntax binary &&
                binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
            {
                return true;
            }
        }

        return false;
    }

    private static void RequireBool(SafeIrExpressionModel expression, ExpressionSyntax syntax)
    {
        if (!string.Equals(expression.Type, SafeIrGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Kernel ShouldHandle expression '{syntax}' must lower to bool.");
        }
    }
}
