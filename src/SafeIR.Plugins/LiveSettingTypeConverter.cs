namespace SafeIR.Plugins;

using System.Globalization;
using SafeIR;

internal static class LiveSettingTypeConverter
{
    public static string FromClrType(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual.IsEnum) {
            return PluginManifestNames.LiveSettingTypes.String;
        }

        if (actual == typeof(bool)) {
            return PluginManifestNames.LiveSettingTypes.Bool;
        }

        if (actual == typeof(int)) {
            return PluginManifestNames.LiveSettingTypes.Int;
        }

        if (actual == typeof(long)) {
            return PluginManifestNames.LiveSettingTypes.Long;
        }

        if (actual == typeof(double)) {
            return PluginManifestNames.LiveSettingTypes.Double;
        }

        if (actual == typeof(string)) {
            return PluginManifestNames.LiveSettingTypes.String;
        }

        throw Diagnostic($"Live setting type '{type.Name}' is not supported.");
    }

    public static SandboxType ToSandboxType(string type)
        => type switch {
            PluginManifestNames.LiveSettingTypes.Bool => SandboxType.Bool,
            PluginManifestNames.LiveSettingTypes.Int => SandboxType.I32,
            PluginManifestNames.LiveSettingTypes.Long => SandboxType.I64,
            PluginManifestNames.LiveSettingTypes.Double => SandboxType.F64,
            PluginManifestNames.LiveSettingTypes.String => SandboxType.String,
            _ => throw Diagnostic($"Live setting type '{type}' is not supported.")
        };

    public static object? DefaultFor(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual == typeof(string)) {
            return string.Empty;
        }

        if (actual.IsEnum) {
            var values = Enum.GetNames(actual);
            return values.Length == 0 ? string.Empty : values[0];
        }

        return Activator.CreateInstance(actual);
    }

    public static object? CoerceClr(Type targetType, object? value)
    {
        var actual = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try {
            return CoerceClrCore(actual, value);
        }
        catch (SandboxValidationException) {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or OverflowException) {
            throw Diagnostic($"Live setting value is not valid for type '{TypeName(actual)}'.");
        }
    }

    private static object? CoerceClrCore(Type actual, object? value)
    {
        if (value is null) {
            return actual == typeof(string) ? string.Empty : Activator.CreateInstance(actual);
        }

        if (actual.IsEnum) {
            return value is string text
                ? Enum.Parse(actual, text, ignoreCase: false)
                : Enum.ToObject(actual, value);
        }

        if (actual == typeof(string)) {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (actual == typeof(bool)) {
            return value is string text ? bool.Parse(text) : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        if (actual == typeof(int)) {
            return value is string text ? int.Parse(text, CultureInfo.InvariantCulture) : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        if (actual == typeof(long)) {
            return value is string text ? long.Parse(text, CultureInfo.InvariantCulture) : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        if (actual == typeof(double)) {
            return FiniteDouble(value);
        }

        throw Diagnostic($"Live setting type '{actual.Name}' is not supported.");
    }

    public static object? CoerceClr(string type, object? value)
        => type switch {
            PluginManifestNames.LiveSettingTypes.Bool => CoerceClr(typeof(bool), value),
            PluginManifestNames.LiveSettingTypes.Int => CoerceClr(typeof(int), value),
            PluginManifestNames.LiveSettingTypes.Long => CoerceClr(typeof(long), value),
            PluginManifestNames.LiveSettingTypes.Double => CoerceClr(typeof(double), value),
            PluginManifestNames.LiveSettingTypes.String => CoerceClr(typeof(string), value),
            _ => throw Diagnostic($"Live setting type '{type}' is not supported.")
        };

    public static SandboxValue ToSandboxValue(string type, object? value)
        => type switch {
            PluginManifestNames.LiveSettingTypes.Bool => SandboxValue.FromBool((bool)CoerceClr(typeof(bool), value)!),
            PluginManifestNames.LiveSettingTypes.Int => SandboxValue.FromInt32((int)CoerceClr(typeof(int), value)!),
            PluginManifestNames.LiveSettingTypes.Long => SandboxValue.FromInt64((long)CoerceClr(typeof(long), value)!),
            PluginManifestNames.LiveSettingTypes.Double => SandboxValue.FromDouble((double)CoerceClr(typeof(double), value)!),
            PluginManifestNames.LiveSettingTypes.String => SandboxValue.FromString((string)CoerceClr(typeof(string), value)!),
            _ => throw Diagnostic($"Live setting type '{type}' is not supported.")
        };

    public static void ValidateRangeDefinition(LiveSettingDefinition definition)
    {
        if (definition.Min is null && definition.Max is null) {
            return;
        }

        if (!PluginManifestNames.LiveSettingTypes.IsNumeric(definition.Type)) {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP022", $"Live setting '{definition.Name}' has a range on non-numeric type '{definition.Type}'.")
            ]);
        }

        if (definition.Min is not null && definition.Max is not null &&
            Number(definition.Min) > Number(definition.Max)) {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP024", $"Live setting '{definition.Name}' has a minimum greater than its maximum.")
            ]);
        }
    }

    public static void ValidateRangeValue(LiveSettingDefinition definition, object? value)
    {
        ValidateRangeDefinition(definition);
        if (definition.Min is not null && Number(value) < Number(definition.Min)) {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP023", $"Live setting '{definition.Name}' is below its allowed range.")
            ]);
        }

        if (definition.Max is not null && Number(value) > Number(definition.Max)) {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP023", $"Live setting '{definition.Name}' is above its allowed range.")
            ]);
        }
    }

    public static Exception Diagnostic(string message)
        => new SandboxValidationException([new SandboxDiagnostic("SGP020", message)]);

    private static double Number(object? value)
        => FiniteDouble(value);

    private static double FiniteDouble(object? value)
    {
        var result = value is string text
            ? double.Parse(text, CultureInfo.InvariantCulture)
            : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(result)) {
            throw Diagnostic("Live setting value must be a finite number.");
        }

        return result;
    }

    private static string TypeName(Type type)
        => type == typeof(int) ? PluginManifestNames.LiveSettingTypes.Int :
           type == typeof(long) ? PluginManifestNames.LiveSettingTypes.Long :
           type == typeof(double) ? PluginManifestNames.LiveSettingTypes.Double :
           type == typeof(bool) ? PluginManifestNames.LiveSettingTypes.Bool :
           type == typeof(string) ? PluginManifestNames.LiveSettingTypes.String :
           type.Name;
}
