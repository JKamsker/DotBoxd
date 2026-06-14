namespace DotBoxd.Plugins.Analyzer;

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
            SpecialType.System_Boolean => DotBoxdGenerationNames.CSharpLiterals.False,
            SpecialType.System_Int32 => DotBoxdGenerationNames.CSharpLiterals.Int32Default,
            SpecialType.System_Int64 => DotBoxdGenerationNames.CSharpLiterals.Int64Default,
            SpecialType.System_Double => DotBoxdGenerationNames.CSharpLiterals.DoubleDefault,
            SpecialType.System_String => DotBoxdGenerationNames.CSharpLiterals.StringDefault,
            _ => DotBoxdGenerationNames.CSharpLiterals.Null
        };
    }

    public static string ObjectLiteral(object? value)
        => value switch {
            null => DotBoxdGenerationNames.CSharpLiterals.Null,
            bool boolean => boolean
                ? DotBoxdGenerationNames.CSharpLiterals.True
                : DotBoxdGenerationNames.CSharpLiterals.False,
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                DotBoxdGenerationNames.CSharpLiterals.Int64Suffix,
            double number when !double.IsNaN(number) && !double.IsInfinity(number) =>
                number.ToString(
                    DotBoxdGenerationNames.CSharpLiterals.DoubleRoundTripFormat,
                    System.Globalization.CultureInfo.InvariantCulture) +
                DotBoxdGenerationNames.CSharpLiterals.DoubleSuffix,
            double => throw new NotSupportedException("Double literal values must be finite."),
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ??
                DotBoxdGenerationNames.CSharpLiterals.Null
        };

    public static string StringLiteral(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
