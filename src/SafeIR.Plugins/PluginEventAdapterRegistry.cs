namespace SafeIR.Plugins;

using System.Reflection;
using SafeIR;

public sealed class PluginEventAdapterRegistry
{
    private readonly Dictionary<Type, object> _adapters = [];

    public void Register<TEvent>(IPluginEventAdapter<TEvent> adapter)
        => _adapters[typeof(TEvent)] = adapter;

    public IPluginEventAdapter<TEvent> Resolve<TEvent>()
    {
        if (_adapters.TryGetValue(typeof(TEvent), out var adapter)) {
            return (IPluginEventAdapter<TEvent>)adapter;
        }

        var discovered = TryDiscoverAdapter<TEvent>() ?? ConventionEventAdapter<TEvent>.Create();
        Register(discovered);
        return discovered;
    }

    private static IPluginEventAdapter<TEvent>? TryDiscoverAdapter<TEvent>()
    {
        var adapterType = typeof(IPluginEventAdapter<TEvent>);
        foreach (var type in typeof(TEvent).Assembly.GetTypes()) {
            if (type.IsAbstract || !adapterType.IsAssignableFrom(type)) {
                continue;
            }

            var instance = StaticInstance(type) ?? Activator.CreateInstance(type);
            return (IPluginEventAdapter<TEvent>)instance!;
        }

        return null;
    }

    private static object? StaticInstance(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(p => string.Equals(p.Name, "Instance", StringComparison.Ordinal) &&
                                 type.IsAssignableFrom(p.PropertyType))
            ?.GetValue(null);
}

internal sealed class ConventionEventAdapter<TEvent> : IPluginEventAdapter<TEvent>
{
    private readonly IReadOnlyList<PropertyInfo> _properties;

    private ConventionEventAdapter(IReadOnlyList<PropertyInfo> properties)
    {
        _properties = properties;
        Parameters = properties
            .Select(p => new Parameter(EventParameterName(p.Name), LiveSettingTypeConverter.ToSandboxType(LiveSettingTypeConverter.FromClrType(p.PropertyType))))
            .ToArray();
    }

    public string EventName => typeof(TEvent).Name;

    public IReadOnlyList<Parameter> Parameters { get; }

    public static ConventionEventAdapter<TEvent> Create()
    {
        var properties = typeof(TEvent)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead)
            .OrderBy(p => p.MetadataToken)
            .ToArray();
        return new ConventionEventAdapter<TEvent>(properties);
    }

    public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e)
        => _properties
            .Select(p => LiveSettingTypeConverter.ToSandboxValue(
                LiveSettingTypeConverter.FromClrType(p.PropertyType),
                p.GetValue(e)))
            .ToArray();

    private static string EventParameterName(string propertyName)
        => "e_" + propertyName;
}
