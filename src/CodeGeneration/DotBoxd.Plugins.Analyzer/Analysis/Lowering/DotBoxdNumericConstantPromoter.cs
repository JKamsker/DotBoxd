namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdNumericConstantPromoter
{
    public static void Promote(
        BinaryExpressionSyntax binary,
        DotBoxdExpressionLoweringContext context,
        ref DotBoxdExpressionModel left,
        ref DotBoxdExpressionModel right)
    {
        if (string.Equals(left.Type, right.Type, StringComparison.Ordinal) ||
            !DotBoxdNumericExpressionLowerer.IsNumeric(left) ||
            !DotBoxdNumericExpressionLowerer.IsNumeric(right)) {
            return;
        }

        if (TryPromoteConstant(binary.Left, context, right.Type) is { } promotedLeft) {
            left = promotedLeft;
            return;
        }

        if (TryPromoteConstant(binary.Right, context, left.Type) is { } promotedRight) {
            right = promotedRight;
        }
    }

    public static DotBoxdExpressionModel? TryPromoteConstant(
        ExpressionSyntax expression,
        DotBoxdExpressionLoweringContext context,
        string targetType)
    {
        try
        {
            return DotBoxdConstantExpressionLowerer.TryLower(
                expression,
                context.SemanticModel,
                context.CancellationToken,
                targetType);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
