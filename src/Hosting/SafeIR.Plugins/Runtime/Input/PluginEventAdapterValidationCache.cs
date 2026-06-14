namespace SafeIR.Plugins;

using System.Runtime.CompilerServices;
using SafeIR;

internal sealed class PluginEventAdapterValidationCache
{
    private readonly ConditionalWeakTable<object, StrongBox<PluginEventShape>> _validatedAdapters = new();

    public void Validate<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IPluginEventAdapter<TEvent> adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        var shape = PluginEventShape.From(adapter);
        if (_validatedAdapters.TryGetValue(adapter, out var cached) &&
            cached.Value.Matches(shape))
        {
            return;
        }

        KernelEntrypointValidator.Validate(manifest, plan, entrypoints, shape);
        _validatedAdapters.AddOrUpdate(adapter, new StrongBox<PluginEventShape>(shape));
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
