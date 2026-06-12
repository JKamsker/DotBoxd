namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrInterpolatedStringExpressionLowerer
{
    public static SafeIrExpressionModel Lower(
        InterpolatedStringExpressionSyntax expression,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var parts = new List<SafeIrExpressionModel>();
        var hasInterpolation = false;
        foreach (var content in expression.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    AddText(parts, text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                    hasInterpolation = true;
                    parts.Add(LowerInterpolation(interpolation, lowerExpression));
                    break;
                default:
                    return Unsupported(expression);
            }
        }

        if (parts.Count == 0)
        {
            return Text(string.Empty);
        }

        if (parts.Count == 1)
        {
            return hasInterpolation ? Concat(Text(string.Empty), parts[0]) : parts[0];
        }

        var current = parts[0];
        for (var i = 1; i < parts.Count; i++)
        {
            current = Concat(current, parts[i]);
        }

        return current;
    }

    private static void AddText(List<SafeIrExpressionModel> parts, string value)
    {
        if (value.Length > 0)
        {
            parts.Add(Text(value));
        }
    }

    private static SafeIrExpressionModel LowerInterpolation(
        InterpolationSyntax interpolation,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        if (interpolation.AlignmentClause is not null ||
            interpolation.FormatClause is not null)
        {
            throw new NotSupportedException("String interpolation alignment and format clauses are not supported.");
        }

        var expression = lowerExpression(interpolation.Expression);
        if (!string.Equals(expression.Type, SafeIrGenerationNames.ManifestTypes.String, StringComparison.Ordinal))
        {
            throw new NotSupportedException("String interpolation holes must lower to string expressions.");
        }

        return expression;
    }

    private static SafeIrExpressionModel Text(string value)
        => new(
            $"{SafeIrGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
            SafeIrGenerationNames.ManifestTypes.String,
            true);

    private static SafeIrExpressionModel Concat(SafeIrExpressionModel left, SafeIrExpressionModel right)
        => new(
            $"{SafeIrGenerationNames.Helpers.ConcatString}({left.Source}, {right.Source})",
            SafeIrGenerationNames.ManifestTypes.String,
            true);

    private static SafeIrExpressionModel Unsupported(InterpolatedStringExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
