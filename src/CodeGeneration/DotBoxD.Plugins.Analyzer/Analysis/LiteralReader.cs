using DotBoxD.CodeGeneration.Shared.Defaults;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class LiteralReader
{
    public static string ParameterDefaultLiteral(IParameterSymbol parameter)
        => ParameterDefaultValueEmitter.ParameterDefaultClause(parameter, DefaultLiteralOptions.Analyzer);

    public static string ObjectDefaultLiteral(ITypeSymbol type, object? value)
        => CSharpLiteralFormatter.FormatValue(value, type, DefaultLiteralOptions.Analyzer) ??
           ObjectLiteral(value);

    public static string DefaultValue(
        ITypeSymbol type,
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is not null)
        {
            var constant = semanticModel.GetConstantValue(expression, cancellationToken);
            if (!constant.HasValue)
            {
                throw new NotSupportedException("Live setting defaults must be compile-time constants.");
            }

            return ObjectLiteral(constant.Value);
        }

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => DotBoxDGenerationNames.CSharpLiterals.False,
            SpecialType.System_Int32 => DotBoxDGenerationNames.CSharpLiterals.Int32Default,
            SpecialType.System_Int64 => DotBoxDGenerationNames.CSharpLiterals.Int64Default,
            SpecialType.System_Double => DotBoxDGenerationNames.CSharpLiterals.DoubleDefault,
            SpecialType.System_String => DotBoxDGenerationNames.CSharpLiterals.StringDefault,
            _ => DotBoxDGenerationNames.CSharpLiterals.Null
        };
    }

    public static string ObjectLiteral(object? value)
        => value switch
        {
            null => DotBoxDGenerationNames.CSharpLiterals.Null,
            bool boolean => boolean
                ? DotBoxDGenerationNames.CSharpLiterals.True
                : DotBoxDGenerationNames.CSharpLiterals.False,
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                DotBoxDGenerationNames.CSharpLiterals.Int64Suffix,
            double number when !double.IsNaN(number) && !double.IsInfinity(number) =>
                number.ToString(
                    DotBoxDGenerationNames.CSharpLiterals.DoubleRoundTripFormat,
                    System.Globalization.CultureInfo.InvariantCulture) +
                DotBoxDGenerationNames.CSharpLiterals.DoubleSuffix,
            double => throw new NotSupportedException("Double literal values must be finite."),
            decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            char character => SymbolDisplay.FormatLiteral(character, quote: true),
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ??
                DotBoxDGenerationNames.CSharpLiterals.Null
        };

    public static string StringLiteral(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

}
