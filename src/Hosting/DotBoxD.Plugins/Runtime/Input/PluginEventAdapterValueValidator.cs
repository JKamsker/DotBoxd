using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Input;

internal static class PluginEventAdapterValueValidator
{
    public static void ValidateValues(
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<SandboxValue> values)
    {
        EnsureValueCountMatches(values.Count, parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
        {
            RequireType(values[i], parameters[i], i);
        }
    }

    public static void ValidateValue(
        IReadOnlyList<Parameter> parameters,
        int eventValueCount,
        int index,
        SandboxValue value)
    {
        EnsureValueCountMatches(eventValueCount, parameters.Count);
        if ((uint)index >= (uint)parameters.Count)
        {
            throw CreateException("Plugin event adapter value index is outside adapter parameters.");
        }

        RequireType(value, parameters[index], index);
    }

    public static void ValidateCopiedValues<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        SandboxValue[] values,
        int destinationIndex)
    {
        var parameters = writer.Parameters;
        ValidateCopiedValues(parameters, writer.EventValueCount, values, destinationIndex);
    }

    public static void ValidateCopiedValues(
        IReadOnlyList<Parameter> parameters,
        int eventValueCount,
        SandboxValue[] values,
        int destinationIndex)
    {
        EnsureValueCountMatches(eventValueCount, parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
        {
            RequireType(values[destinationIndex + i], parameters[i], i);
        }
    }

    private static void EnsureValueCountMatches(int valueCount, int parameterCount)
    {
        if (valueCount != parameterCount)
        {
            throw CreateException("Plugin event adapter value count does not match adapter parameters.");
        }
    }

    private static void RequireType(SandboxValue value, Parameter parameter, int index)
    {
        var message =
            "Plugin event adapter output for parameter '" +
            parameter.Name +
            "' at index " +
            index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " does not match adapter parameter type '" +
            parameter.Type +
            "'.";

        try
        {
            SandboxValueValidator.RequireType(value, parameter.Type, SandboxErrorCode.InvalidInput, message);
        }
        catch (SandboxRuntimeException)
        {
            throw CreateException(message);
        }
    }

    private static SandboxValidationException CreateException(string message) =>
        new([
            new SandboxDiagnostic(PluginEventAdapterShapeValidator.DiagnosticCode, message)
        ]);
}
