using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static partial class DotBoxDConditionBodyModelFactory
{
    public static DotBoxDStatementBodyModel Create(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
        => LowerCondition(expression, ReturnBool(value: true), ReturnBool(value: false), context);

    public static DotBoxDStatementBodyModel CreateBranch(
        ExpressionSyntax condition,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
        => LowerCondition(condition, whenTrue, whenFalse, context);

    /// <summary>An unconditional <c>return true</c> body — used by lowered hook chains with no Where.</summary>
    public static DotBoxDStatementBodyModel AlwaysTrue() => ReturnBool(value: true);

    /// <summary>An unconditional <c>return false</c> body — the AND-compose false branch for chains.</summary>
    public static DotBoxDStatementBodyModel AlwaysFalse() => ReturnBool(value: false);

    private static DotBoxDStatementBodyModel LowerCondition(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                LowerCondition(parenthesized.Expression, whenTrue, whenFalse, context),
            ConditionalExpressionSyntax conditional =>
                LowerConditional(conditional, whenTrue, whenFalse, context),
            PrefixUnaryExpressionSyntax unary when unary.Kind() == SyntaxKind.LogicalNotExpression =>
                LowerNot(unary, whenTrue, whenFalse, context),
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

    private static DotBoxDStatementBodyModel LowerConditional(
        ConditionalExpressionSyntax conditional,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        if (DotBoxDPatternExpressionLowerer.TryLowerDeclarationPattern(
                conditional.Condition,
                context,
                part => DotBoxDExpressionModelFactory.Create(part, context),
                out var condition,
                out var captureName,
                out var capture))
        {
            var trueContext = context.WithPatternCapture(captureName, capture);
            return If(
                condition.Source,
                LowerCondition(conditional.WhenTrue, whenTrue, whenFalse, trueContext),
                LowerCondition(conditional.WhenFalse, whenTrue, whenFalse, context),
                condition.Allocates);
        }

        if (DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(conditional.Condition))
        {
            throw new NotSupportedException($"Unsupported declaration-pattern composition '{conditional.Condition}'.");
        }

        return LowerCondition(
            conditional.Condition,
            LowerCondition(conditional.WhenTrue, whenTrue, whenFalse, context),
            LowerCondition(conditional.WhenFalse, whenTrue, whenFalse, context),
            context);
    }

    private static DotBoxDStatementBodyModel LowerNot(
        PrefixUnaryExpressionSyntax unary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        if (DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(unary.Operand))
        {
            throw new NotSupportedException($"Unsupported declaration-pattern composition '{unary}'.");
        }

        return LowerCondition(unary.Operand, whenFalse, whenTrue, context);
    }

    private static DotBoxDStatementBodyModel LowerLeafCondition(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        if (ContainsOrderedBooleanOperator(expression))
        {
            throw new NotSupportedException(
                $"Unsupported ordered bool expression inside '{expression}'.");
        }

        var condition = DotBoxDExpressionModelFactory.Create(expression, context);
        RequireBool(condition, expression);
        return If(condition.Source, whenTrue, whenFalse, condition.Allocates);
    }

    private static DotBoxDStatementBodyModel If(
        string condition,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        bool conditionAllocates)
    {
        var source =
            $"new {StatementArrayType()} {{ new {TypeNames.GlobalIfStatement}(" +
            $"{condition}, {whenTrue.Source}, {whenFalse.Source}, Span) }}";
        return new DotBoxDStatementBodyModel(
            source,
            conditionAllocates || whenTrue.Allocates || whenFalse.Allocates);
    }

    private static DotBoxDStatementBodyModel ReturnBool(bool value)
        => ReturnExpression(
            $"{DotBoxDGenerationNames.Helpers.Bool}({BoolLiteral(value)})",
            allocates: false);

    private static DotBoxDStatementBodyModel ReturnExpression(string expression, bool allocates)
        => new(
            $"new {StatementArrayType()} {{ new {TypeNames.GlobalReturnStatement}({expression}, Span) }}",
            allocates);

    private static DotBoxDStatementBodyModel AssignBool(string name, bool value)
    {
        var source =
            $"new {StatementArrayType()} {{ new {TypeNames.GlobalAssignmentStatement}(" +
            $"{LiteralReader.StringLiteral(name)}, " +
            $"{DotBoxDGenerationNames.Helpers.Bool}({BoolLiteral(value)}), Span) }}";
        return new DotBoxDStatementBodyModel(source, false);
    }

    private static DotBoxDStatementBodyModel Concat(
        DotBoxDStatementBodyModel first,
        DotBoxDStatementBodyModel second)
        => new(
            TypeNames.GlobalEnumerable + ".ToArray(" +
            TypeNames.GlobalEnumerable + ".Concat<" + TypeNames.GlobalStatement + ">(" +
            $"{first.Source}, {second.Source}))",
            first.Allocates || second.Allocates);

    private static string StatementArrayType() => TypeNames.GlobalStatement + "[]";

    private static string Var(string name)
        => $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})";

    private static string ConditionTemp(ExpressionSyntax expression)
        => "$dotboxd.condition." +
           expression.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string BoolLiteral(bool value)
        => value
            ? DotBoxDGenerationNames.CSharpLiterals.True
            : DotBoxDGenerationNames.CSharpLiterals.False;

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

    private static void RequireBool(DotBoxDExpressionModel expression, ExpressionSyntax syntax)
    {
        if (!string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Kernel ShouldHandle expression '{syntax}' must lower to bool.");
        }
    }
}
