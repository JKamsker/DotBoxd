namespace SafeIR.Plugins;

using System.Globalization;
using SafeIR;

internal static class LiveSettingTypeConverter
{
    public static string FromClrType(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual.IsEnum) {
            return "string";
        }

        if (actual == typeof(bool)) {
            return "bool";
        }

        if (actual == typeof(int)) {
            return "int";
        }

        if (actual == typeof(long)) {
            return "long";
        }

        if (actual == typeof(double)) {
            return "double";
        }

        if (actual == typeof(string)) {
            return "string";
        }

        throw Diagnostic($"Live setting type '{type.Name}' is not supported.");
    }

    public static SandboxType ToSandboxType(string type)
        => type switch {
            "bool" => SandboxType.Bool,
            "int" => SandboxType.I32,
            "long" => SandboxType.I64,
            "double" => SandboxType.F64,
            "string" => SandboxType.String,
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
            return value is string text ? double.Parse(text, CultureInfo.InvariantCulture) : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        throw Diagnostic($"Live setting type '{targetType.Name}' is not supported.");
    }

    public static object? CoerceClr(string type, object? value)
        => type switch {
            "bool" => CoerceClr(typeof(bool), value),
            "int" => CoerceClr(typeof(int), value),
            "long" => CoerceClr(typeof(long), value),
            "double" => CoerceClr(typeof(double), value),
            "string" => CoerceClr(typeof(string), value),
            _ => throw Diagnostic($"Live setting type '{type}' is not supported.")
        };

    public static SandboxValue ToSandboxValue(string type, object? value)
        => type switch {
            "bool" => SandboxValue.FromBool(Convert.ToBoolean(value, CultureInfo.InvariantCulture)),
            "int" => SandboxValue.FromInt32(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            "long" => SandboxValue.FromInt64(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            "double" => SandboxValue.FromDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            "string" => SandboxValue.FromString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            _ => throw Diagnostic($"Live setting type '{type}' is not supported.")
        };

    public static void ValidateRangeDefinition(LiveSettingDefinition definition)
    {
        if (definition.Min is null && definition.Max is null) {
            return;
        }

        if (definition.Type is not ("int" or "long" or "double")) {
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
        => Convert.ToDouble(value, CultureInfo.InvariantCulture);
}
