using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDNumericConstantPromoter
{
    public static void Promote(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context,
        ref DotBoxDExpressionModel left,
        ref DotBoxDExpressionModel right)
    {
        if (string.Equals(left.Type, right.Type, StringComparison.Ordinal) ||
            !DotBoxDNumericExpressionLowerer.IsNumeric(left) ||
            !DotBoxDNumericExpressionLowerer.IsNumeric(right))
        {
            return;
        }

        if (TryPromoteConstant(binary.Left, context, right.Type) is { } promotedLeft)
        {
            left = promotedLeft;
            return;
        }

        if (TryPromoteConstant(binary.Right, context, left.Type) is { } promotedRight)
        {
            right = promotedRight;
        }
    }

    public static DotBoxDExpressionModel? TryPromoteConstant(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        string targetType)
    {
        try
        {
            return DotBoxDConstantExpressionLowerer.TryLower(
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
