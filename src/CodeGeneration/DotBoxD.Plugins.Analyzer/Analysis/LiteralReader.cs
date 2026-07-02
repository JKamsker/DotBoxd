using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class LiteralReader
{
    public static string ParameterDefaultLiteral(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return string.Empty;
        }

        return " = " + ObjectDefaultLiteral(parameter.Type, parameter.ExplicitDefaultValue);
    }

    public static string ObjectDefaultLiteral(ITypeSymbol type, object? value)
    {
        if (value is null && type.IsValueType)
        {
            return "default";
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return value is null
                ? "default"
                : "unchecked((" + TypeName(type) + ")" + EnumSignedLiteral((INamedTypeSymbol)type, value) + ")";
        }

        if (NullableEnumUnderlying(type) is { } nullableEnum)
        {
            return ObjectDefaultLiteral(nullableEnum, value);
        }

        if (type.SpecialType == SpecialType.System_Single && value is float number)
        {
            if (float.IsNaN(number))
            {
                return "global::System.Single.NaN";
            }

            if (float.IsPositiveInfinity(number))
            {
                return "global::System.Single.PositiveInfinity";
            }

            if (float.IsNegativeInfinity(number))
            {
                return "global::System.Single.NegativeInfinity";
            }

            return number.ToString(
                DotBoxDGenerationNames.CSharpLiterals.DoubleRoundTripFormat,
                System.Globalization.CultureInfo.InvariantCulture) + "f";
        }

        if (type.SpecialType == SpecialType.System_Double && value is double doubleNumber)
        {
            if (double.IsNaN(doubleNumber))
            {
                return "global::System.Double.NaN";
            }

            if (double.IsPositiveInfinity(doubleNumber))
            {
                return "global::System.Double.PositiveInfinity";
            }

            if (double.IsNegativeInfinity(doubleNumber))
            {
                return "global::System.Double.NegativeInfinity";
            }
        }

        if (type.SpecialType == SpecialType.System_DateTime &&
            value is DateTime dateTime &&
            dateTime == default)
        {
            // Metadata optional DateTime defaults arrive boxed, not as a source literal. Re-emit the
            // equivalent source default instead of an invalid culture-formatted DateTime string.
            return "default";
        }

        return ObjectLiteral(value);
    }

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

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static ITypeSymbol? NullableEnumUnderlying(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named ||
            named.ConstructedFrom.SpecialType != SpecialType.System_Nullable_T ||
            named.TypeArguments.Length != 1 ||
            named.TypeArguments[0].TypeKind != TypeKind.Enum)
        {
            return null;
        }

        return named.TypeArguments[0];
    }

    private static string EnumSignedLiteral(INamedTypeSymbol enumType, object value)
    {
        var raw = enumType.EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_UInt64 => unchecked((long)(ulong)value),
            SpecialType.System_UInt32 => unchecked((int)(uint)value),
            SpecialType.System_Int64 => (long)value,
            SpecialType.System_Int32 => (int)value,
            SpecialType.System_UInt16 => (ushort)value,
            SpecialType.System_Int16 => (short)value,
            SpecialType.System_Byte => (byte)value,
            SpecialType.System_SByte => (sbyte)value,
            _ => throw new NotSupportedException(
                $"Enum literal values for '{enumType.ToDisplayString()}' are not supported.")
        };
        return raw.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               (enumType.EnumUnderlyingType?.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64
                   ? DotBoxDGenerationNames.CSharpLiterals.Int64Suffix
                   : string.Empty);
    }
}
