using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.CodeGeneration.Shared.Defaults;

internal static class CSharpLiteralFormatter
{
    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string? FormatValue(object? value, ITypeSymbol type, DefaultLiteralOptions options)
    {
        if (value is null)
        {
            return type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "null"
                : "default";
        }

        var enumType = EnumType(type);
        if (enumType is not null)
        {
            return FormatEnumLiteral(enumType, value, options);
        }

        if (FormatPrimitiveLiteral(value, options) is { } literal)
        {
            return literal;
        }

        return type.IsValueType && IsRuntimeDefaultValue(value) ? "default" : null;
    }

    public static bool TryFormatAttributeValue(TypedConstant argument, out string literal)
    {
        if (argument.IsNull)
        {
            literal = "null";
            return true;
        }

        if (argument.Kind == TypedConstantKind.Enum &&
            argument.Type is not null &&
            argument.Value is not null &&
            FormatPrimitiveLiteral(argument.Value, DefaultLiteralOptions.SourceGenerator) is { } enumValue)
        {
            literal = "(" + argument.Type.ToDisplayString(s_qualifiedFormat) + ")" + enumValue;
            return true;
        }

        if (argument.Value is not null &&
            FormatPrimitiveLiteral(argument.Value, DefaultLiteralOptions.SourceGenerator) is { } value)
        {
            literal = value;
            return true;
        }

        literal = string.Empty;
        return false;
    }

    public static string? FormatPrimitiveLiteral(object value, DefaultLiteralOptions options)
        => value switch
        {
            bool b => b ? "true" : "false",
            string s => "\"" + EscapeStringLiteral(s) + "\"",
            char c => "'" + EscapeCharLiteral(c) + "'",
            sbyte v => v.ToString(CultureInfo.InvariantCulture),
            byte v => v.ToString(CultureInfo.InvariantCulture),
            short v => v.ToString(CultureInfo.InvariantCulture),
            ushort v => v.ToString(CultureInfo.InvariantCulture),
            int v => v.ToString(CultureInfo.InvariantCulture),
            uint v => v.ToString(CultureInfo.InvariantCulture) + Suffix("U", options),
            long v => v.ToString(CultureInfo.InvariantCulture) + Suffix("L", options),
            ulong v => v.ToString(CultureInfo.InvariantCulture) + Suffix("UL", options),
            float v => FormatSingleLiteral(v, options),
            double v => FormatDoubleLiteral(v, options),
            decimal v => v.ToString(CultureInfo.InvariantCulture) + Suffix("M", options),
            _ => null,
        };

    public static string EscapeStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            AppendEscapedStringCharacter(builder, c);
        }

        return builder.ToString();
    }

    private static ITypeSymbol? EnumType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return type;
        }

        return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable &&
            nullable.TypeArguments[0].TypeKind == TypeKind.Enum
                ? nullable.TypeArguments[0]
                : null;
    }

    private static string? FormatEnumLiteral(ITypeSymbol enumType, object value, DefaultLiteralOptions options)
    {
        var enumValue = options.UseUncheckedEnumCasts && enumType is INamedTypeSymbol namedEnum
            ? FormatSignedEnumLiteral(namedEnum, value, options)
            : FormatPrimitiveLiteral(value, options);
        if (enumValue is null)
        {
            return null;
        }

        var cast = "(" + enumType.ToDisplayString(s_qualifiedFormat) + ")" + enumValue;
        return options.UseUncheckedEnumCasts ? "unchecked(" + cast + ")" : cast;
    }

    private static string FormatSignedEnumLiteral(
        INamedTypeSymbol enumType,
        object value,
        DefaultLiteralOptions options)
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
            _ => throw new System.NotSupportedException(
                $"Enum literal values for '{enumType.ToDisplayString()}' are not supported.")
        };
        return raw.ToString(CultureInfo.InvariantCulture) +
            (enumType.EnumUnderlyingType?.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64
                ? Suffix("L", options)
                : string.Empty);
    }

    private static string FormatSingleLiteral(float value, DefaultLiteralOptions options)
    {
        if (float.IsNaN(value))
        {
            return "global::System.Single.NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "global::System.Single.PositiveInfinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "global::System.Single.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + Suffix("F", options);
    }

    private static string FormatDoubleLiteral(double value, DefaultLiteralOptions options)
    {
        if (double.IsNaN(value))
        {
            return "global::System.Double.NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "global::System.Double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "global::System.Double.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + Suffix("D", options);
    }

    private static string EscapeCharLiteral(char c)
        => c switch
        {
            '\'' => "\\'",
            '\\' => "\\\\",
            '\0' => "\\0",
            '\a' => "\\a",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\v' => "\\v",
            _ => char.IsControl(c) || c == 0x2028 || c == 0x2029
                ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture)
                : c.ToString(),
        };

    private static void AppendEscapedStringCharacter(StringBuilder builder, char c)
    {
        switch (c)
        {
            case '\\':
                builder.Append("\\\\");
                break;
            case '"':
                builder.Append("\\\"");
                break;
            case '\a':
                builder.Append("\\a");
                break;
            case '\b':
                builder.Append("\\b");
                break;
            case '\f':
                builder.Append("\\f");
                break;
            case '\v':
                builder.Append("\\v");
                break;
            case '\r':
                builder.Append("\\r");
                break;
            case '\n':
                builder.Append("\\n");
                break;
            case '\u0085':
                builder.Append("\\u0085");
                break;
            case '\u2028':
                builder.Append("\\u2028");
                break;
            case '\u2029':
                builder.Append("\\u2029");
                break;
            case '\t':
                builder.Append("\\t");
                break;
            case '\0':
                builder.Append("\\0");
                break;
            default:
                if (char.IsControl(c))
                {
                    builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(c);
                }

                break;
        }
    }

    private static bool IsRuntimeDefaultValue(object value)
    {
        var type = value.GetType();
        return Equals(value, System.Activator.CreateInstance(type));
    }

    private static string Suffix(string suffix, DefaultLiteralOptions options)
        => options.LowercaseNumericSuffixes ? suffix.ToLowerInvariant() : suffix;
}
