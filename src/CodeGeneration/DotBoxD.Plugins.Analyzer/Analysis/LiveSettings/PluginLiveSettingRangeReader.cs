using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginLiveSettingRangeReader
{
    public static (string? Min, string? Max) Read(IPropertySymbol property, string type)
    {
        var range = RangeAttribute(property);
        if (range is null ||
            range.ConstructorArguments.Length < DotBoxDGenerationNames.RangeAttributeArguments.NumericOverloadCount)
        {
            return (null, null);
        }

        if (!DotBoxDGenerationNames.ManifestTypes.IsNumeric(type))
        {
            throw new NotSupportedException(
                $"Live setting '{property.Name}' has a range on non-numeric type '{type}'.");
        }

        var values = RangeValues(property, type, range);
        if (MinimumGreaterThanMaximum(values.Min, values.Max, type))
        {
            throw new NotSupportedException(
                $"Live setting '{property.Name}' has a minimum greater than its maximum.");
        }

        return (LiteralReader.ObjectLiteral(values.Min), LiteralReader.ObjectLiteral(values.Max));
    }

    private static AttributeData? RangeAttribute(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.RangeAttribute,
                    StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    private static (object? Min, object? Max) RangeValues(
        IPropertySymbol property,
        string type,
        AttributeData range)
    {
        if (range.ConstructorArguments.Length == DotBoxDGenerationNames.RangeAttributeArguments.NumericOverloadCount)
        {
            return (
                RangeValue(
                    range.ConstructorArguments[DotBoxDGenerationNames.RangeAttributeArguments.NumericMinimumIndex].Value,
                    type),
                RangeValue(
                    range.ConstructorArguments[DotBoxDGenerationNames.RangeAttributeArguments.NumericMaximumIndex].Value,
                    type));
        }

        if (range.ConstructorArguments.Length == DotBoxDGenerationNames.RangeAttributeArguments.TypeAndStringOverloadCount &&
            range.ConstructorArguments[DotBoxDGenerationNames.RangeAttributeArguments.ConversionTypeIndex].Value is INamedTypeSymbol conversionType &&
            string.Equals(DotBoxDTypeNameReader.LiveSettingTypeName(conversionType), type, StringComparison.Ordinal))
        {
            return (
                RangeValue(
                    range.ConstructorArguments[DotBoxDGenerationNames.RangeAttributeArguments.ConvertedMinimumIndex].Value,
                    type),
                RangeValue(
                    range.ConstructorArguments[DotBoxDGenerationNames.RangeAttributeArguments.ConvertedMaximumIndex].Value,
                    type));
        }

        throw new NotSupportedException(
            $"Live setting '{property.Name}' uses an unsupported RangeAttribute overload.");
    }

    private static object RangeValue(object? value, string type)
    {
        try
        {
            if (string.Equals(type, DotBoxDGenerationNames.ManifestTypes.Int, StringComparison.Ordinal))
            {
                return IntRangeValue(value);
            }

            if (string.Equals(type, DotBoxDGenerationNames.ManifestTypes.Long, StringComparison.Ordinal))
            {
                return LongRangeValue(value);
            }

            if (string.Equals(type, DotBoxDGenerationNames.ManifestTypes.Double, StringComparison.Ordinal))
            {
                return DoubleRangeValue(value);
            }

            throw RangeValueException();
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw RangeValueException(ex);
        }
    }

    private static int IntRangeValue(object? value)
        => value switch
        {
            int number => number,
            double number when IsWhole(number) && number >= int.MinValue && number <= int.MaxValue => (int)number,
            string text => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            _ => throw RangeValueException()
        };

    private static long LongRangeValue(object? value)
        => value switch
        {
            int number => number,
            long number => number,
            double number when IsWhole(number) && number >= long.MinValue && number <= long.MaxValue => (long)number,
            string text => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            _ => throw RangeValueException()
        };

    private static double DoubleRangeValue(object? value)
    {
        var number = value switch
        {
            int integer => integer,
            long integer => integer,
            double floating => floating,
            string text => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => throw RangeValueException()
        };
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            throw RangeValueException();
        }

        return number;
    }

    private static bool MinimumGreaterThanMaximum(object? min, object? max, string type)
    {
        if (string.Equals(type, DotBoxDGenerationNames.ManifestTypes.Int, StringComparison.Ordinal))
        {
            return (int)min! > (int)max!;
        }

        if (string.Equals(type, DotBoxDGenerationNames.ManifestTypes.Long, StringComparison.Ordinal))
        {
            return (long)min! > (long)max!;
        }

        return (double)min! > (double)max!;
    }

    private static bool IsWhole(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Truncate(value) == value;

    private static NotSupportedException RangeValueException(Exception? inner = null)
        => new("Live setting ranges must be finite numeric values matching the live setting type.", inner);
}
