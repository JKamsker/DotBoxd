using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDPatternCaptureExpressionLowerer
{
    public static DotBoxDExpressionModel? TryLower(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionLoweringContext, DotBoxDExpressionModel> lowerExpression)
    {
        if (binary.Kind() == SyntaxKind.IsExpression)
        {
            return DotBoxDPatternExpressionLowerer.LowerIsTypeExpression(
                binary,
                context,
                part => lowerExpression(part, context));
        }

        if (binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression &&
            DotBoxDPatternExpressionLowerer.ContainsDeclarationPattern(binary))
        {
            throw new NotSupportedException($"Unsupported declaration-pattern composition '{binary}'.");
        }

        return null;
    }

    public static bool TryLowerIdentifier(
        string name,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (context.TryGetPatternCapture(name, out var capture))
        {
            lowered = capture.Key;
            return true;
        }

        lowered = null!;
        return false;
    }
}
