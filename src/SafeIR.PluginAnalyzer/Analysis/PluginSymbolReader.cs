namespace SafeIR.PluginAnalyzer;

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class PluginSymbolReader
{
    public static string? PluginId(IReadOnlyList<AttributeData> attributes)
    {
        for (var i = 0; i < attributes.Count; i++) {
            var attribute = attributes[i];
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    SafeIrGenerationNames.Metadata.GamePluginAttribute,
                    StringComparison.Ordinal)) {
                return attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value as string
                    : null;
            }
        }

        return null;
    }

    public static IReadOnlyList<INamedTypeSymbol> EventTypes(INamedTypeSymbol kernelType)
    {
        var eventTypes = new List<INamedTypeSymbol>();
        foreach (var implementedInterface in kernelType.AllInterfaces) {
            if (IsEventKernelInterface(implementedInterface) &&
                implementedInterface.TypeArguments.Length > 0 &&
                implementedInterface.TypeArguments[0] is INamedTypeSymbol eventType)
            {
                eventTypes.Add(eventType);
            }
        }

        return eventTypes;
    }

    private static bool IsEventKernelInterface(INamedTypeSymbol type)
        => string.Equals(
            type.OriginalDefinition.ToDisplayString(),
            SafeIrGenerationNames.Metadata.EventKernelInterface,
            StringComparison.Ordinal);

    public static EquatableArray<EventPropertyModel> EventProperties(INamedTypeSymbol eventType)
    {
        var properties = PluginEventPropertyReader.Read(eventType);
        if (properties.Length == 0) {
            return default;
        }

        var models = new EventPropertyModel[properties.Length];
        for (var i = 0; i < properties.Length; i++) {
            var property = properties[i];
            models[i] = new EventPropertyModel(property.Name, SafeIrTypeNameReader.SandboxTypeName(property.Type));
        }

        return EquatableArray<EventPropertyModel>.FromOwned(models);
    }

    public static EquatableArray<LiveSettingModel> LiveSettings(
        INamedTypeSymbol kernelType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var count = CountLiveSettingProperties(kernelType);
        if (count == 0) {
            return default;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var settings = new LiveSettingModel[count];
        var index = 0;
        foreach (var property in LiveSettingProperties(kernelType)) {
            if (!names.Add(property.Name)) {
                throw new NotSupportedException($"Live setting '{property.Name}' is declared more than once.");
            }

            settings[index] = ToLiveSetting(property, semanticModel, cancellationToken);
            index++;
        }

        return EquatableArray<LiveSettingModel>.FromOwned(settings);
    }

    private static IEnumerable<IPropertySymbol> LiveSettingProperties(INamedTypeSymbol kernelType)
    {
        for (var current = kernelType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property && IsLiveSetting(property))
                {
                    ValidateLiveSettingProperty(property);
                    yield return property;
                }
            }
        }
    }

    private static int CountLiveSettingProperties(INamedTypeSymbol kernelType)
    {
        var count = 0;
        foreach (var property in LiveSettingProperties(kernelType)) {
            _ = property;
            count++;
        }

        return count;
    }

    private static bool IsLiveSetting(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    SafeIrGenerationNames.Metadata.LiveSettingAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateLiveSettingProperty(IPropertySymbol property)
    {
        if (property.DeclaredAccessibility != Accessibility.Public ||
            property.IsStatic ||
            property.Parameters.Length != 0 ||
            property.GetMethod is null ||
            property.SetMethod is null ||
            property.GetMethod.DeclaredAccessibility != Accessibility.Public ||
            property.SetMethod.DeclaredAccessibility != Accessibility.Public ||
            property.SetMethod.IsInitOnly)
        {
            throw new NotSupportedException(
                $"Live setting '{property.Name}' must be a public instance property with public get and set accessors.");
        }
    }

    private static LiveSettingModel ToLiveSetting(
        IPropertySymbol property,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var syntax = DeclaringPropertySyntax(property, cancellationToken);
        var type = SafeIrTypeNameReader.SandboxTypeName(property.Type);
        var range = Range(property, type);
        return new LiveSettingModel(
            property.Name,
            type,
            LiteralReader.DefaultValue(property.Type, syntax?.Initializer?.Value, semanticModel, cancellationToken),
            range.Min,
            range.Max);
    }

    private static PropertyDeclarationSyntax? DeclaringPropertySyntax(
        IPropertySymbol property,
        CancellationToken cancellationToken)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax syntax)
            {
                return syntax;
            }
        }

        return null;
    }

    private static (string? Min, string? Max) Range(IPropertySymbol property, string type)
    {
        var range = RangeAttribute(property);
        if (range is null ||
            range.ConstructorArguments.Length < SafeIrGenerationNames.RangeAttributeArguments.NumericOverloadCount) {
            return (null, null);
        }

        if (!SafeIrGenerationNames.ManifestTypes.IsNumeric(type))
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
                    SafeIrGenerationNames.Metadata.RangeAttribute,
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
        if (range.ConstructorArguments.Length == SafeIrGenerationNames.RangeAttributeArguments.NumericOverloadCount)
        {
            return (
                RangeValue(
                    range.ConstructorArguments[SafeIrGenerationNames.RangeAttributeArguments.NumericMinimumIndex].Value,
                    type),
                RangeValue(
                    range.ConstructorArguments[SafeIrGenerationNames.RangeAttributeArguments.NumericMaximumIndex].Value,
                    type));
        }

        if (range.ConstructorArguments.Length == SafeIrGenerationNames.RangeAttributeArguments.TypeAndStringOverloadCount &&
            range.ConstructorArguments[SafeIrGenerationNames.RangeAttributeArguments.ConversionTypeIndex].Value is INamedTypeSymbol conversionType &&
            string.Equals(SafeIrTypeNameReader.SandboxTypeName(conversionType), type, StringComparison.Ordinal))
        {
            return (
                RangeValue(
                    range.ConstructorArguments[SafeIrGenerationNames.RangeAttributeArguments.ConvertedMinimumIndex].Value,
                    type),
                RangeValue(
                    range.ConstructorArguments[SafeIrGenerationNames.RangeAttributeArguments.ConvertedMaximumIndex].Value,
                    type));
        }

        throw new NotSupportedException(
            $"Live setting '{property.Name}' uses an unsupported RangeAttribute overload.");
    }

    private static object RangeValue(object? value, string type)
    {
        try
        {
            if (string.Equals(type, SafeIrGenerationNames.ManifestTypes.Int, StringComparison.Ordinal))
            {
                return IntRangeValue(value);
            }

            if (string.Equals(type, SafeIrGenerationNames.ManifestTypes.Long, StringComparison.Ordinal))
            {
                return LongRangeValue(value);
            }

            if (string.Equals(type, SafeIrGenerationNames.ManifestTypes.Double, StringComparison.Ordinal))
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
        if (string.Equals(type, SafeIrGenerationNames.ManifestTypes.Int, StringComparison.Ordinal))
        {
            return (int)min! > (int)max!;
        }

        if (string.Equals(type, SafeIrGenerationNames.ManifestTypes.Long, StringComparison.Ordinal))
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
