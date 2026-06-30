using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Input;

internal static class PluginEventValueWriterValueValidator
{
    public static void ValidateValue<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        int index,
        SandboxValue value)
    {
        var parameters = writer.Parameters;
        EnsureValueCountMatches(writer.EventValueCount, parameters.Count);
        if ((uint)index >= (uint)parameters.Count)
        {
            throw CreateException("Plugin event value writer index is outside adapter parameters.");
        }

        RequireType(value, parameters[index], index);
    }

    public static void ValidateCopiedValues<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        SandboxValue[] values,
        int destinationIndex)
    {
        var parameters = writer.Parameters;
        EnsureValueCountMatches(writer.EventValueCount, parameters.Count);
        for (var i = 0; i < parameters.Count; i++)
        {
            RequireType(values[destinationIndex + i], parameters[i], i);
        }
    }

    private static void EnsureValueCountMatches(int valueCount, int parameterCount)
    {
        if (valueCount != parameterCount)
        {
            throw CreateException("Plugin event value writer count does not match adapter parameters.");
        }
    }

    private static void RequireType(SandboxValue value, Parameter parameter, int index)
    {
        var message =
            "Plugin event value writer output for parameter '" +
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
            new SandboxDiagnostic(PluginEventValueWriterShapeValidator.DiagnosticCode, message)
        ]);
}
