using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Input;

internal sealed class PluginEventAdapterValidationCache
{
    private readonly ConditionalWeakTable<object, StrongBox<PluginEventShape>> _validatedAdapters = new();
    private readonly ConditionalWeakTable<object, StrongBox<PluginEventShape>> _validatedLocalCallbackAdapters = new();

    public void Validate<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IPluginEventAdapter<TEvent> adapter)
        => Validate(
            manifest,
            plan,
            entrypoints,
            adapter,
            _validatedAdapters,
            KernelEntrypointValidator.Validate);

    public void ValidateLocalCallback<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IPluginEventAdapter<TEvent> adapter)
        => Validate(
            manifest,
            plan,
            entrypoints,
            adapter,
            _validatedLocalCallbackAdapters,
            KernelEntrypointValidator.ValidateLocalCallback);

    private static void Validate<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IPluginEventAdapter<TEvent> adapter,
        ConditionalWeakTable<object, StrongBox<PluginEventShape>> validatedAdapters,
        Action<PluginManifest, ExecutionPlan, KernelEntrypoints, PluginEventShape> validateEntrypoints)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        var eventName = adapter.EventName;
        var parameters = adapter.Parameters;
        PluginEventValueWriterShapeValidator.Validate(adapter, parameters);
        if (validatedAdapters.TryGetValue(adapter, out var cached) &&
            cached.Value.Matches(eventName, parameters))
        {
            return;
        }

        var shape = new PluginEventShape(eventName, parameters);
        validateEntrypoints(manifest, plan, entrypoints, shape);
        validatedAdapters.AddOrUpdate(adapter, new StrongBox<PluginEventShape>(shape));
    }
}

internal static class PluginEventValueWriterShapeValidator
{
    public static void Validate<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        IReadOnlyList<Parameter> parameters)
    {
        if (adapter is IPluginEventValueWriter<TEvent> writer &&
            writer.EventValueCount != parameters.Count)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK036", "Plugin event value writer count does not match adapter parameters.")
            ]);
        }
    }
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
