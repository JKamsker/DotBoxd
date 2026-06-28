namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

internal static class RpcEnumRangeGuardSource
{
    public static void AppendInt64EnumRangeGuard(
        StringBuilder builder,
        INamedTypeSymbol enumType,
        string indent,
        string message)
    {
        if (enumType.EnumUnderlyingType?.SpecialType == SpecialType.System_UInt32)
        {
            AppendRangeGuard(builder, indent, "uint.MinValue", "uint.MaxValue", message);
            return;
        }

        if (enumType.EnumUnderlyingType?.SpecialType == SpecialType.System_UInt64)
        {
            AppendUInt64NegativeGuard(builder, enumType, indent, message);
        }
    }

    private static void AppendRangeGuard(
        StringBuilder builder,
        string indent,
        string minimum,
        string maximum,
        string message)
    {
        builder.Append(indent).Append("if (__value < ").Append(minimum).Append(" || __value > ").Append(maximum)
            .AppendLine(")");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    throw new global::System.NotSupportedException(")
            .Append(LiteralReader.StringLiteral(message)).AppendLine(");");
        builder.Append(indent).AppendLine("}");
    }

    private static void AppendUInt64NegativeGuard(
        StringBuilder builder,
        INamedTypeSymbol enumType,
        string indent,
        string message)
    {
        builder.Append(indent).AppendLine("if (__value < 0)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).AppendLine("    var __bits = unchecked((ulong)__value);");
        if (HasFlagsAttribute(enumType))
        {
            builder.Append(indent).Append("    if ((__bits & ~").Append(UInt64Literal(DeclaredMask(enumType)))
                .AppendLine(") != 0UL)");
        }
        else
        {
            AppendDefinedNegativeValueGuard(builder, enumType, indent);
        }

        builder.Append(indent).AppendLine("    {");
        builder.Append(indent).Append("        throw new global::System.NotSupportedException(")
            .Append(LiteralReader.StringLiteral(message)).AppendLine(");");
        builder.Append(indent).AppendLine("    }");
        builder.Append(indent).AppendLine("}");
    }

    private static void AppendDefinedNegativeValueGuard(
        StringBuilder builder,
        INamedTypeSymbol enumType,
        string indent)
    {
        var values = DeclaredValues(enumType)
            .Where(static value => value > long.MaxValue)
            .Distinct()
            .OrderBy(static value => value)
            .ToArray();
        if (values.Length == 0)
        {
            builder.Append(indent).AppendLine("    if (true)");
            return;
        }

        builder.Append(indent).Append("    if (");
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(" && ");
            }

            builder.Append("__bits != ").Append(UInt64Literal(values[i]));
        }

        builder.AppendLine(")");
    }

    private static bool HasFlagsAttribute(INamedTypeSymbol enumType)
        => enumType.GetAttributes().Any(static attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            "System.FlagsAttribute",
            StringComparison.Ordinal));

    private static ulong DeclaredMask(INamedTypeSymbol enumType)
    {
        ulong mask = 0;
        foreach (var value in DeclaredValues(enumType))
        {
            mask |= value;
        }

        return mask;
    }

    private static IEnumerable<ulong> DeclaredValues(INamedTypeSymbol enumType)
    {
        foreach (var member in enumType.GetMembers())
        {
            if (member is not IFieldSymbol { HasConstantValue: true } field ||
                field.ConstantValue is null)
            {
                continue;
            }

            yield return Convert.ToUInt64(field.ConstantValue, CultureInfo.InvariantCulture);
        }
    }

    private static string UInt64Literal(ulong value)
        => value.ToString(CultureInfo.InvariantCulture) + "UL";
}
