namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdInterpolatedStringExpressionLowerer
{
    public static DotBoxdExpressionModel Lower(
        InterpolatedStringExpressionSyntax expression,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        var parts = new List<DotBoxdExpressionModel>();
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

    private static void AddText(List<DotBoxdExpressionModel> parts, string value)
    {
        if (value.Length > 0)
        {
            parts.Add(Text(value));
        }
    }

    private static DotBoxdExpressionModel LowerInterpolation(
        InterpolationSyntax interpolation,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        if (interpolation.AlignmentClause is not null ||
            interpolation.FormatClause is not null)
        {
            throw new NotSupportedException("String interpolation alignment and format clauses are not supported.");
        }

        var expression = lowerExpression(interpolation.Expression);
        if (!string.Equals(expression.Type, DotBoxdGenerationNames.ManifestTypes.String, StringComparison.Ordinal))
        {
            throw new NotSupportedException("String interpolation holes must lower to string expressions.");
        }

        return expression;
    }

    private static DotBoxdExpressionModel Text(string value)
        => new(
            $"{DotBoxdGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
            DotBoxdGenerationNames.ManifestTypes.String,
            true);

    private static DotBoxdExpressionModel Concat(DotBoxdExpressionModel left, DotBoxdExpressionModel right)
        => new(
            $"{DotBoxdGenerationNames.Helpers.ConcatString}({left.Source}, {right.Source})",
            DotBoxdGenerationNames.ManifestTypes.String,
            true);

    private static DotBoxdExpressionModel Unsupported(InterpolatedStringExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
