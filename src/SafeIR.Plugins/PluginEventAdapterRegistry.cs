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
        var properties = ReadableProperties(typeof(TEvent));
        return new ConventionEventAdapter<TEvent>(properties);
    }

    private static IReadOnlyList<PropertyInfo> ReadableProperties(Type eventType)
    {
        var properties = eventType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead)
            .Where(p => p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken)
            .ToArray();

        return TryConstructorPropertyOrder(eventType, properties, out var ordered)
            ? ordered
            : properties;
    }

    private static bool TryConstructorPropertyOrder(
        Type eventType,
        IReadOnlyList<PropertyInfo> properties,
        out IReadOnlyList<PropertyInfo> ordered)
    {
        var byName = properties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var constructor in eventType.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 0 || parameters.Length != properties.Count)
            {
                continue;
            }

            var selected = new PropertyInfo[parameters.Length];
            var matched = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.Name is null ||
                    !byName.TryGetValue(parameter.Name, out var property) ||
                    property.PropertyType != parameter.ParameterType)
                {
                    matched = false;
                    break;
                }

                selected[i] = property;
            }

            if (matched)
            {
                ordered = selected;
                return true;
            }
        }

        ordered = [];
        return false;
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
