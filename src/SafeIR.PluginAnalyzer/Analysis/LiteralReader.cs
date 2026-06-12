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
            SpecialType.System_Boolean => SafeIrGenerationNames.CSharpLiterals.False,
            SpecialType.System_Int32 => SafeIrGenerationNames.CSharpLiterals.Int32Default,
            SpecialType.System_Int64 => SafeIrGenerationNames.CSharpLiterals.Int64Default,
            SpecialType.System_Double => SafeIrGenerationNames.CSharpLiterals.DoubleDefault,
            SpecialType.System_String => SafeIrGenerationNames.CSharpLiterals.StringDefault,
            _ => SafeIrGenerationNames.CSharpLiterals.Null
        };
    }

    public static string ObjectLiteral(object? value)
        => value switch {
            null => SafeIrGenerationNames.CSharpLiterals.Null,
            bool boolean => boolean
                ? SafeIrGenerationNames.CSharpLiterals.True
                : SafeIrGenerationNames.CSharpLiterals.False,
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                SafeIrGenerationNames.CSharpLiterals.Int64Suffix,
            double number when !double.IsNaN(number) && !double.IsInfinity(number) =>
                number.ToString(
                    SafeIrGenerationNames.CSharpLiterals.DoubleRoundTripFormat,
                    System.Globalization.CultureInfo.InvariantCulture) +
                SafeIrGenerationNames.CSharpLiterals.DoubleSuffix,
            double => throw new NotSupportedException("Double literal values must be finite."),
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ??
                SafeIrGenerationNames.CSharpLiterals.Null
        };

    public static string StringLiteral(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
