namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class LiteralReader
{
    public static string DefaultValue(
        ITypeSymbol type,
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is not null) {
            var constant = semanticModel.GetConstantValue(expression, cancellationToken);
            if (!constant.HasValue) {
                throw new NotSupportedException("Live setting defaults must be compile-time constants.");
            }

            return ObjectLiteral(constant.Value);
        }

        return type.SpecialType switch {
            SpecialType.System_Boolean => "false",
            SpecialType.System_Int32 => "0",
            SpecialType.System_Int64 => "0L",
            SpecialType.System_Double => "0D",
            SpecialType.System_String => "\"\"",
            _ => "null"
        };
    }

    public static string ObjectLiteral(object? value)
        => value switch {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            double number when !double.IsNaN(number) && !double.IsInfinity(number) =>
                number.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "D",
            double => throw new NotSupportedException("Double literal values must be finite."),
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null"
        };

    public static string StringLiteral(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    public static string? LiteralExpression(ExpressionSyntax expression)
        => expression switch {
            LiteralExpressionSyntax literal => literal.Token.Value switch {
                string text => $"Str({StringLiteral(text)})",
                int number => $"I32({number.ToString(System.Globalization.CultureInfo.InvariantCulture)})",
                bool boolean => $"Bool({(boolean ? "true" : "false")})",
                _ => null
            },
            _ when expression.IsKind(SyntaxKind.TrueLiteralExpression) => "Bool(true)",
            _ when expression.IsKind(SyntaxKind.FalseLiteralExpression) => "Bool(false)",
            _ => null
        };
}
