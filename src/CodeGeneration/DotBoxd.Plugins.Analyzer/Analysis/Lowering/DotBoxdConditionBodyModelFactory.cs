namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdConditionBodyModelFactory
{
    public static DotBoxdStatementBodyModel Create(
        ExpressionSyntax expression,
        DotBoxdExpressionLoweringContext context)
        => LowerCondition(expression, ReturnBool(value: true), ReturnBool(value: false), context);

    public static DotBoxdStatementBodyModel CreateBranch(
        ExpressionSyntax condition,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
        => LowerCondition(condition, whenTrue, whenFalse, context);

    /// <summary>An unconditional <c>return true</c> body — used by lowered hook chains with no Where.</summary>
    public static DotBoxdStatementBodyModel AlwaysTrue() => ReturnBool(value: true);

    /// <summary>An unconditional <c>return false</c> body — the AND-compose false branch for chains.</summary>
    public static DotBoxdStatementBodyModel AlwaysFalse() => ReturnBool(value: false);

    private static DotBoxdStatementBodyModel LowerCondition(
        ExpressionSyntax expression,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
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

    private static DotBoxdStatementBodyModel LowerAnd(
        BinaryExpressionSyntax binary,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
        => LowerCondition(
            binary.Left,
            LowerCondition(binary.Right, whenTrue, whenFalse, context),
            whenFalse,
            context);

    private static DotBoxdStatementBodyModel LowerOr(
        BinaryExpressionSyntax binary,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
        => LowerCondition(
            binary.Left,
            whenTrue,
            LowerCondition(binary.Right, whenTrue, whenFalse, context),
            context);

    private static DotBoxdStatementBodyModel LowerConditional(
        ConditionalExpressionSyntax conditional,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
        => LowerCondition(
            conditional.Condition,
            LowerCondition(conditional.WhenTrue, whenTrue, whenFalse, context),
            LowerCondition(conditional.WhenFalse, whenTrue, whenFalse, context),
            context);

    private static DotBoxdStatementBodyModel LowerEagerAnd(
        BinaryExpressionSyntax binary,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
        => LowerEagerBoolBinary(
            binary,
            DotBoxdGenerationNames.Helpers.And,
            whenTrue,
            whenFalse,
            context);

    private static DotBoxdStatementBodyModel LowerEagerOr(
        BinaryExpressionSyntax binary,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
        => LowerEagerBoolBinary(
            binary,
            DotBoxdGenerationNames.Helpers.Or,
            whenTrue,
            whenFalse,
            context);

    private static DotBoxdStatementBodyModel LowerBoolXor(
        BinaryExpressionSyntax binary,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
        => LowerEagerBoolBinary(
            binary,
            DotBoxdGenerationNames.Helpers.Ne,
            whenTrue,
            whenFalse,
            context);

    private static DotBoxdStatementBodyModel LowerBoolEquality(
        BinaryExpressionSyntax binary,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
    {
        var helper = binary.Kind() == SyntaxKind.NotEqualsExpression
            ? DotBoxdGenerationNames.Helpers.Ne
            : DotBoxdGenerationNames.Helpers.Eq;
        return LowerEagerBoolBinary(binary, helper, whenTrue, whenFalse, context);
    }

    private static DotBoxdStatementBodyModel LowerEagerBoolBinary(
        BinaryExpressionSyntax binary,
        string helper,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
    {
        var leftName = ConditionTemp(binary.Left);
        var rightName = ConditionTemp(binary.Right);
        var leftLowered = LowerConditionValue(binary.Left, leftName, context);
        var rightLowered = LowerConditionValue(binary.Right, rightName, context);
        var result = new DotBoxdExpressionModel(
            $"{helper}({Var(leftName)}, {Var(rightName)})",
            DotBoxdGenerationNames.ManifestTypes.Bool,
            false);

        return Concat(leftLowered, Concat(rightLowered, If(result.Source, whenTrue, whenFalse, false)));
    }

    private static DotBoxdStatementBodyModel LowerConditionValue(
        ExpressionSyntax expression,
        string name,
        DotBoxdExpressionLoweringContext context)
        => Concat(
            AssignBool(name, value: false),
            LowerCondition(expression, AssignBool(name, value: true), AssignBool(name, value: false), context));

    private static DotBoxdStatementBodyModel LowerLeafCondition(
        ExpressionSyntax expression,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        DotBoxdExpressionLoweringContext context)
    {
        if (ContainsOrderedBooleanOperator(expression))
        {
            throw new NotSupportedException(
                $"Unsupported ordered bool expression inside '{expression}'.");
        }

        var condition = DotBoxdExpressionModelFactory.Create(expression, context);
        RequireBool(condition, expression);
        return If(condition.Source, whenTrue, whenFalse, condition.Allocates);
    }

    private static DotBoxdStatementBodyModel If(
        string condition,
        DotBoxdStatementBodyModel whenTrue,
        DotBoxdStatementBodyModel whenFalse,
        bool conditionAllocates)
    {
        var source =
            $"new global::DotBoxd.Kernels.Statement[] {{ new {DotBoxdGenerationNames.IrTypes.IfStatement}(" +
            $"{condition}, {whenTrue.Source}, {whenFalse.Source}, Span) }}";
        return new DotBoxdStatementBodyModel(
            source,
            conditionAllocates || whenTrue.Allocates || whenFalse.Allocates);
    }

    private static DotBoxdStatementBodyModel ReturnBool(bool value)
        => ReturnExpression(
            $"{DotBoxdGenerationNames.Helpers.Bool}({BoolLiteral(value)})",
            allocates: false);

    private static DotBoxdStatementBodyModel ReturnExpression(string expression, bool allocates)
        => new(
            $"new global::DotBoxd.Kernels.Statement[] {{ new {DotBoxdGenerationNames.IrTypes.ReturnStatement}({expression}, Span) }}",
            allocates);

    private static DotBoxdStatementBodyModel AssignBool(string name, bool value)
    {
        var source =
            "new global::DotBoxd.Kernels.Statement[] { new global::DotBoxd.Kernels.AssignmentStatement(" +
            $"{LiteralReader.StringLiteral(name)}, " +
            $"{DotBoxdGenerationNames.Helpers.Bool}({BoolLiteral(value)}), Span) }}";
        return new DotBoxdStatementBodyModel(source, false);
    }

    private static DotBoxdStatementBodyModel Concat(
        DotBoxdStatementBodyModel first,
        DotBoxdStatementBodyModel second)
        => new(
            "global::System.Linq.Enumerable.ToArray(" +
            "global::System.Linq.Enumerable.Concat<global::DotBoxd.Kernels.Statement>(" +
            $"{first.Source}, {second.Source}))",
            first.Allocates || second.Allocates);

    private static string Var(string name)
        => $"{DotBoxdGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})";

    private static string ConditionTemp(ExpressionSyntax expression)
        => "$dotboxd.condition." +
           expression.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string BoolLiteral(bool value)
        => value
            ? DotBoxdGenerationNames.CSharpLiterals.True
            : DotBoxdGenerationNames.CSharpLiterals.False;

    private static bool IsBoolEquality(
        BinaryExpressionSyntax binary,
        DotBoxdExpressionLoweringContext context)
    {
        if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
        {
            return false;
        }

        return IsBool(binary.Left, context) && IsBool(binary.Right, context);
    }

    private static bool IsEagerAnd(
        BinaryExpressionSyntax binary,
        DotBoxdExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.BitwiseAndExpression && IsBoolOperands(binary, context);

    private static bool IsEagerOr(
        BinaryExpressionSyntax binary,
        DotBoxdExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.BitwiseOrExpression && IsBoolOperands(binary, context);

    private static bool IsBoolXor(
        BinaryExpressionSyntax binary,
        DotBoxdExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.ExclusiveOrExpression && IsBoolOperands(binary, context);

    private static bool IsBoolOperands(
        BinaryExpressionSyntax binary,
        DotBoxdExpressionLoweringContext context)
        => IsBool(binary.Left, context) && IsBool(binary.Right, context);

    private static bool IsBool(ExpressionSyntax expression, DotBoxdExpressionLoweringContext context)
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

    private static void RequireBool(DotBoxdExpressionModel expression, ExpressionSyntax syntax)
    {
        if (!string.Equals(expression.Type, DotBoxdGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Kernel ShouldHandle expression '{syntax}' must lower to bool.");
        }
    }
}
