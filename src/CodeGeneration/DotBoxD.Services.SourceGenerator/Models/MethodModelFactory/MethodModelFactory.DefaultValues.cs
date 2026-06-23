using System.Globalization;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    /// <summary>
    /// Formats a non-cancellation-token parameter's explicit default value as the C# literal to emit
    /// in a generated signature, or <see langword="null"/> when it cannot be safely expressed - in
    /// which case the caller emits no default rather than a wrong one (preserving prior behaviour).
    /// </summary>
    private static string? FormatDefaultValueLiteral(IParameterSymbol param)
    {
        if (!param.HasExplicitDefaultValue)
        {
            return null;
        }

        var value = param.ExplicitDefaultValue;
        var type = param.Type;

        if (value is null)
        {
            // "= null" for reference / nullable value types; "= default" for a non-nullable value
            // type's default (always valid and produces the same value).
            return type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "null"
                : "default";
        }

        // Enum default: cast the (boxed underlying) constant to the fully-qualified enum type so the
        // literal is unambiguous regardless of the generated file's usings. Unwrap Nullable<TEnum>.
        var underlyingType = type;
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable)
        {
            underlyingType = nullable.TypeArguments[0];
        }

        if (underlyingType.TypeKind == TypeKind.Enum)
        {
            var underlyingLiteral = FormatPrimitiveLiteral(value);
            return underlyingLiteral is null
                ? null
                : "(" + underlyingType.ToDisplayString(s_qualifiedFormat) + ")" + underlyingLiteral;
        }

        return FormatPrimitiveLiteral(value);
    }

    private static string? FormatPrimitiveLiteral(object value) => value switch
    {
        bool b => b ? "true" : "false",
        string s => "\"" + LiteralHelpers.EscapeStringLiteral(s) + "\"",
        char c => "'" + EscapeCharLiteral(c) + "'",
        sbyte v => v.ToString(CultureInfo.InvariantCulture),
        byte v => v.ToString(CultureInfo.InvariantCulture),
        short v => v.ToString(CultureInfo.InvariantCulture),
        ushort v => v.ToString(CultureInfo.InvariantCulture),
        int v => v.ToString(CultureInfo.InvariantCulture),
        uint v => v.ToString(CultureInfo.InvariantCulture) + "U",
        long v => v.ToString(CultureInfo.InvariantCulture) + "L",
        ulong v => v.ToString(CultureInfo.InvariantCulture) + "UL",
        // NaN/Infinity have no literal form; fall back to "no default" rather than emit invalid code.
        float v => float.IsNaN(v) || float.IsInfinity(v) ? null : v.ToString("R", CultureInfo.InvariantCulture) + "F",
        double v => double.IsNaN(v) || double.IsInfinity(v) ? null : v.ToString("R", CultureInfo.InvariantCulture) + "D",
        decimal v => v.ToString(CultureInfo.InvariantCulture) + "M",
        _ => null,
    };

    private static string EscapeCharLiteral(char c) => c switch
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
        // U+2028 (LINE SEPARATOR) and U+2029 (PARAGRAPH SEPARATOR) are line terminators inside a char
        // literal (CS1010) but are NOT control chars, so route them through the same \uXXXX escape path.
        // Mirrors LiteralHelpers.EscapeStringLiteral, which escapes both code points explicitly.
        _ => char.IsControl(c) || c == 0x2028 || c == 0x2029
            ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture)
            : c.ToString(),
    };
}
