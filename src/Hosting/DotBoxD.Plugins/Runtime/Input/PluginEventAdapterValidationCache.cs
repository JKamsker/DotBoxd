using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Input;

internal sealed class PluginEventAdapterValidationCache
{
    private readonly ConditionalWeakTable<object, StrongBox<PluginEventShape>> _validatedAdapters = new();

    public IReadOnlyList<Parameter> Validate<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IPluginEventAdapter<TEvent> adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        var eventName = adapter.EventName;
        var parameters = adapter.Parameters;
        PluginEventAdapterShapeValidator.Validate(adapter, eventName, parameters);
        if (_validatedAdapters.TryGetValue(adapter, out var cached) &&
            cached.Value.Matches(eventName, parameters))
        {
            return parameters;
        }

        var shape = new PluginEventShape(eventName, parameters);
        KernelEntrypointValidator.Validate<TEvent>(manifest, plan, entrypoints, shape);
        _validatedAdapters.AddOrUpdate(adapter, new StrongBox<PluginEventShape>(shape));
        return parameters;
    }
}

internal static class PluginEventAdapterShapeValidator
{
    internal const string DiagnosticCode = "DBXK036";

    public static void Validate<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        string eventName,
        IReadOnlyList<Parameter> parameters)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            throw CreateException("Plugin event adapter event name must be non-empty.");
        }

        if (parameters is null)
        {
            throw CreateException("Plugin event adapter parameters must be non-null.");
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter is null)
            {
                throw CreateException("Plugin event adapter parameters must not contain null entries.");
            }

            if (string.IsNullOrEmpty(parameter.Name))
            {
                throw CreateException("Plugin event adapter parameter names must be non-empty.");
            }
        }

        if (adapter is IPluginEventValueWriter<TEvent> writer &&
            writer.EventValueCount != parameters.Count)
        {
            throw CreateException("Plugin event value writer count does not match adapter parameters.");
        }
    }

    private static SandboxValidationException CreateException(string message) =>
        new([
            new SandboxDiagnostic(DiagnosticCode, message)
        ]);
}

internal readonly struct PluginEventShape
{
    public PluginEventShape(string eventName, IReadOnlyList<Parameter> parameters)
    {
        EventName = eventName;
        Parameters = Copy(parameters);
    }

    public string EventName { get; }
    public IReadOnlyList<Parameter> Parameters { get; }

    public static PluginEventShape From<TEvent>(IPluginEventAdapter<TEvent> adapter)
        => new(adapter.EventName, adapter.Parameters);

    public bool Matches(string eventName, IReadOnlyList<Parameter> parameters)
    {
        if (!string.Equals(EventName, eventName, StringComparison.Ordinal) ||
            Parameters.Count != parameters.Count)
        {
            return false;
        }

        for (var i = 0; i < Parameters.Count; i++)
        {
            if (Parameters[i] != parameters[i])
            {
                return false;
            }
        }

        return true;
    }

    public bool Matches(PluginEventShape other)
    {
        if (!string.Equals(EventName, other.EventName, StringComparison.Ordinal) ||
            Parameters.Count != other.Parameters.Count)
        {
            return false;
        }

        for (var i = 0; i < Parameters.Count; i++)
        {
            if (Parameters[i] != other.Parameters[i])
            {
                return false;
            }
        }

        return true;
    }

    private static Parameter[] Copy(IReadOnlyList<Parameter> parameters)
    {
        if (parameters.Count == 0)
        {
            return [];
        }

        var copy = new Parameter[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            copy[i] = parameters[i];
        }

        return copy;
    }
}
