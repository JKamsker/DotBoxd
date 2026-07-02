using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DotBoxD.CodeGeneration.Shared.Defaults;

internal static class ParameterDefaultMetadataReader
{
    public static bool HasOptionalMetadata(IParameterSymbol parameter)
        => parameter.IsOptional || HasOptionalAttribute(parameter);

    public static bool HasDateTimeConstantAttribute(IParameterSymbol parameter)
        => TryGetDateTimeConstantTicks(parameter, out _);

    public static bool HasDecimalConstantAttribute(IParameterSymbol parameter)
        => TryGetDecimalConstantParts(parameter, out _, out _, out _, out _, out _);

    public static bool HasDefaultParameterValueAttribute(IParameterSymbol parameter)
        => TryFormatDefaultParameterValueAttributeLiteral(parameter, out _);

    public static bool HasUnresolvedMetadataDefaultAttribute(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            // Concrete readers match framework metadata names first. Any remaining lookalike
            // metadata-default attribute fails closed instead of becoming a plain optional default.
            if (attribute.AttributeClass?.Name is
                "DateTimeConstantAttribute" or
                "DecimalConstantAttribute" or
                "DefaultParameterValueAttribute")
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetDateTimeConstantTicks(IParameterSymbol parameter, out long ticks)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() ==
                "System.Runtime.CompilerServices.DateTimeConstantAttribute" &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is long value)
            {
                ticks = value;
                return true;
            }
        }

        ticks = 0;
        return false;
    }

    public static string FormatDateTimeConstantAttribute(IParameterSymbol parameter)
        => TryGetDateTimeConstantTicks(parameter, out var ticks)
            ? "[global::System.Runtime.CompilerServices.DateTimeConstantAttribute(" +
              ticks.ToString(CultureInfo.InvariantCulture) + "L)] "
            : string.Empty;

    public static string FormatDecimalConstantAttribute(IParameterSymbol parameter)
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

        return "[global::System.Runtime.CompilerServices.DecimalConstantAttribute(" +
            scale.ToString(CultureInfo.InvariantCulture) +
            ", " +
            sign.ToString(CultureInfo.InvariantCulture) +
            ", " +
            FormatDecimalAttributeIntArgument(high) +
            ", " +
            FormatDecimalAttributeIntArgument(middle) +
            ", " +
            FormatDecimalAttributeIntArgument(low) +
            ")] ";
    }

    public static string FormatDefaultParameterValueAttribute(IParameterSymbol parameter)
        => TryFormatDefaultParameterValueAttributeLiteral(parameter, out var literal)
            ? "[global::System.Runtime.InteropServices.DefaultParameterValueAttribute(" + literal + ")] "
            : string.Empty;

    public static string FormatDecimalConstantMetadataDefaultValueExpression(IParameterSymbol parameter)
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

    public static bool TryGetDecimalConstantDefault(IParameterSymbol parameter, out decimal value)
    {
        if (TryGetDecimalConstantParts(
            parameter,
            out var scale,
            out var sign,
            out var high,
            out var middle,
            out var low))
        {
            value = new decimal(
                unchecked((int)low),
                unchecked((int)middle),
                unchecked((int)high),
                sign != 0,
                scale);
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryFormatDefaultParameterValueAttributeLiteral(
        IParameterSymbol parameter,
        out string literal)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() !=
                "System.Runtime.InteropServices.DefaultParameterValueAttribute" ||
                attribute.ConstructorArguments.Length != 1)
            {
                continue;
            }

            return CSharpLiteralFormatter.TryFormatAttributeValue(attribute.ConstructorArguments[0], out literal);
        }

        literal = string.Empty;
        return false;
    }

    public static bool TryGetDefaultParameterValueAttributeValue(
        IParameterSymbol parameter,
        out object? value)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() ==
                "System.Runtime.InteropServices.DefaultParameterValueAttribute" &&
                attribute.ConstructorArguments.Length == 1)
            {
                value = attribute.ConstructorArguments[0].Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool HasOptionalAttribute(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass?.Name == "OptionalAttribute" &&
                attributeClass.ContainingNamespace.ToDisplayString() == "System.Runtime.InteropServices")
            {
                return true;
            }
        }

        return false;
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
