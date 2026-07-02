using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static bool HasDecimalConstantAttribute(IParameterSymbol parameter) =>
        TryGetDecimalConstantParts(parameter, out _, out _, out _, out _, out _);

    private static string FormatDecimalConstantMetadataDefaultValueExpression(IParameterSymbol parameter)
    {
        if (!TryGetDecimalConstantParts(
            parameter,
            out var scale,
            out var sign,
            out var high,
            out var middle,
            out var low))
        {
            return string.Empty;
        }

        return "new global::System.Decimal(" +
            FormatDecimalConstructorIntArgument(low) +
            ", " +
            FormatDecimalConstructorIntArgument(middle) +
            ", " +
            FormatDecimalConstructorIntArgument(high) +
            ", " +
            (sign == 0 ? "false" : "true") +
            ", " +
            scale.ToString(CultureInfo.InvariantCulture) +
            ")";
    }

    private static void AppendDecimalConstantAttribute(StringBuilder sb, IParameterSymbol parameter)
    {
        if (!TryGetDecimalConstantParts(
            parameter,
            out var scale,
            out var sign,
            out var high,
            out var middle,
            out var low))
        {
            return;
        }

        sb.Append("[global::System.Runtime.CompilerServices.DecimalConstantAttribute(")
            .Append(scale.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(sign.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(FormatDecimalAttributeIntArgument(high))
            .Append(", ")
            .Append(FormatDecimalAttributeIntArgument(middle))
            .Append(", ")
            .Append(FormatDecimalAttributeIntArgument(low))
            .Append(")] ");
    }

    private static bool TryGetDecimalConstantParts(
        IParameterSymbol parameter,
        out byte scale,
        out byte sign,
        out uint high,
        out uint middle,
        out uint low)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() !=
                "System.Runtime.CompilerServices.DecimalConstantAttribute" ||
                attribute.ConstructorArguments.Length != 5)
            {
                continue;
            }

            if (TryGetByteAttributeArgument(attribute.ConstructorArguments[0], out scale) &&
                TryGetByteAttributeArgument(attribute.ConstructorArguments[1], out sign) &&
                TryGetUInt32AttributeArgument(attribute.ConstructorArguments[2], out high) &&
                TryGetUInt32AttributeArgument(attribute.ConstructorArguments[3], out middle) &&
                TryGetUInt32AttributeArgument(attribute.ConstructorArguments[4], out low))
            {
                return true;
            }
        }

        scale = 0;
        sign = 0;
        high = 0;
        middle = 0;
        low = 0;
        return false;
    }

    private static bool TryGetByteAttributeArgument(TypedConstant argument, out byte value)
    {
        if (argument.Value is byte byteValue)
        {
            value = byteValue;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetUInt32AttributeArgument(TypedConstant argument, out uint value)
    {
        switch (argument.Value)
        {
            case int intValue:
                value = unchecked((uint)intValue);
                return true;

            case uint uintValue:
                value = uintValue;
                return true;

            default:
                value = 0;
                return false;
        }
    }

    private static string FormatDecimalConstructorIntArgument(uint value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        return value <= int.MaxValue
            ? text
            : "unchecked((int)" + text + "U)";
    }

    private static string FormatDecimalAttributeIntArgument(uint value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        return value <= int.MaxValue
            ? text
            : text + "U";
    }
}
