using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static partial class DotBoxDConditionBodyModelFactory
{
    private static DotBoxDStatementBodyModel LowerEagerAnd(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
        => LowerEagerBoolBinary(
            binary,
            DotBoxDGenerationNames.Helpers.And,
            whenTrue,
            whenFalse,
            context);

    private static DotBoxDStatementBodyModel LowerEagerOr(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
        => LowerEagerBoolBinary(
            binary,
            DotBoxDGenerationNames.Helpers.Or,
            whenTrue,
            whenFalse,
            context);

    private static DotBoxDStatementBodyModel LowerBoolXor(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
        => LowerEagerBoolBinary(
            binary,
            DotBoxDGenerationNames.Helpers.Ne,
            whenTrue,
            whenFalse,
            context);

    private static DotBoxDStatementBodyModel LowerBoolEquality(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        var helper = binary.Kind() == SyntaxKind.NotEqualsExpression
            ? DotBoxDGenerationNames.Helpers.Ne
            : DotBoxDGenerationNames.Helpers.Eq;
        return LowerEagerBoolBinary(binary, helper, whenTrue, whenFalse, context);
    }

    private static DotBoxDStatementBodyModel LowerEagerBoolBinary(
        BinaryExpressionSyntax binary,
        string helper,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context)
    {
        var leftName = ConditionTemp(binary.Left);
        var rightName = ConditionTemp(binary.Right);
        var leftLowered = LowerConditionValue(binary.Left, leftName, context);
        var rightLowered = LowerConditionValue(binary.Right, rightName, context);
        var result = new DotBoxDExpressionModel(
            $"{helper}({Var(leftName)}, {Var(rightName)})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            false);

        return Concat(leftLowered, Concat(rightLowered, If(result.Source, whenTrue, whenFalse, false)));
    }

    private static DotBoxDStatementBodyModel LowerConditionValue(
        ExpressionSyntax expression,
        string name,
        DotBoxDExpressionLoweringContext context)
        => Concat(
            AssignBool(name, value: false),
            LowerCondition(expression, AssignBool(name, value: true), AssignBool(name, value: false), context));

    private static bool IsBoolEquality(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context)
    {
        if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
        {
            return false;
        }

        return IsBool(binary.Left, context) && IsBool(binary.Right, context);
    }

    private static bool IsEagerAnd(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.BitwiseAndExpression && IsBoolOperands(binary, context);

    private static bool IsEagerOr(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.BitwiseOrExpression && IsBoolOperands(binary, context);

    private static bool IsBoolXor(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context)
        => binary.Kind() == SyntaxKind.ExclusiveOrExpression && IsBoolOperands(binary, context);

    private static bool IsBoolOperands(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context)
        => IsBool(binary.Left, context) && IsBool(binary.Right, context);

    private static bool IsBool(ExpressionSyntax expression, DotBoxDExpressionLoweringContext context)
        => context.SemanticModel
            .GetTypeInfo(expression, context.CancellationToken)
            .ConvertedType?.SpecialType == SpecialType.System_Boolean;
}
