namespace SafeIR.Plugins;

using System.Reflection;
using SafeIR;

public sealed class PluginEventAdapterRegistry
{
    private readonly Dictionary<Type, RegisteredPluginEventAdapter> _adapters = [];

    public void Register<TEvent>(IPluginEventAdapter<TEvent> adapter)
        => _adapters[typeof(TEvent)] = new(
            adapter,
            new PluginEventShape(adapter.EventName, adapter.Parameters));

    public IPluginEventAdapter<TEvent> Resolve<TEvent>()
    {
        if (_adapters.TryGetValue(typeof(TEvent), out var registered)) {
            return (IPluginEventAdapter<TEvent>)registered.Adapter;
        }

        var discovered = TryDiscoverAdapter<TEvent>() ?? ConventionEventAdapter<TEvent>.Create();
        Register(discovered);
        return discovered;
    }

    internal bool TryResolveShape(string eventName, out PluginEventShape shape)
    {
        foreach (var adapter in _adapters.Values)
        {
            var current = adapter.Shape;
            if (string.Equals(current.EventName, eventName, StringComparison.Ordinal))
            {
                shape = current;
                return true;
            }
        }

        shape = default!;
        return false;
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

internal readonly record struct RegisteredPluginEventAdapter(object Adapter, PluginEventShape Shape);

internal interface IPluginEventValueWriter<in TEvent>
{
    int EventValueCount { get; }
    SandboxValue ToSandboxValue(TEvent e, int index);
    void CopySandboxValues(TEvent e, SandboxValue[] destination, int destinationIndex);
}

internal sealed class ConventionEventAdapter<TEvent> : IPluginEventAdapter<TEvent>, IPluginEventValueWriter<TEvent>
{
    private readonly ConventionEventProperty<TEvent>[] _properties;

    private ConventionEventAdapter(IReadOnlyList<PropertyInfo> properties)
    {
        _properties = new ConventionEventProperty<TEvent>[properties.Count];
        var parameters = new Parameter[properties.Count];
        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            var settingType = LiveSettingTypeConverter.FromClrType(property.PropertyType);
            _properties[i] = new ConventionEventProperty<TEvent>(
                settingType,
                CreateGetter(property));
            parameters[i] = new Parameter(
                EventParameterName(property.Name),
                LiveSettingTypeConverter.ToSandboxType(settingType));
        }

        Parameters = parameters;
    }

    public string EventName => typeof(TEvent).Name;

    public IReadOnlyList<Parameter> Parameters { get; }

    public int EventValueCount => _properties.Length;

    public static ConventionEventAdapter<TEvent> Create()
    {
        var properties = ReadableProperties(typeof(TEvent));
        return new ConventionEventAdapter<TEvent>(properties);
    }

    private static IReadOnlyList<PropertyInfo> ReadableProperties(Type eventType)
    {
        var properties = ReadablePropertiesInHierarchy(eventType).ToArray();
        ValidatePropertyNames(properties);

        return TryConstructorPropertyOrder(eventType, properties, out var ordered)
            ? ordered
            : properties;
    }

    private static void ValidatePropertyNames(IReadOnlyList<PropertyInfo> properties)
    {
        var names = new Dictionary<string, string>(properties.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < properties.Count; i++)
        {
            var propertyName = properties[i].Name;
            if (names.TryGetValue(propertyName, out var firstName))
            {
                throw new NotSupportedException(
                    $"Event property '{firstName}' is declared more than once or differs only by case.");
            }

            names.Add(propertyName, propertyName);
        }
    }

    private static IEnumerable<PropertyInfo> ReadablePropertiesInHierarchy(Type eventType)
    {
        var hierarchy = new Stack<Type>();
        for (var current = eventType; current is not null && current != typeof(object); current = current.BaseType)
        {
            hierarchy.Push(current);
        }

        while (hierarchy.Count > 0)
        {
            var current = hierarchy.Pop();
            var properties = current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            Array.Sort(properties, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (var property in properties)
            {
                if (property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)
                {
                    yield return property;
                }
            }
        }
    }

    private static bool TryConstructorPropertyOrder(
        Type eventType,
        IReadOnlyList<PropertyInfo> properties,
        out IReadOnlyList<PropertyInfo> ordered)
    {
        foreach (var constructor in eventType.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 0 || parameters.Length != properties.Count)
            {
                continue;
            }

            if (MatchesDeclaredPropertyOrder(parameters, properties))
            {
                ordered = properties;
                return true;
            }

            if (ReorderedConstructorProperties(parameters, properties) is { } selected)
            {
                ordered = selected;
                return true;
            }
        }

        ordered = [];
        return false;
    }

    private static bool MatchesDeclaredPropertyOrder(
        IReadOnlyList<ParameterInfo> parameters,
        IReadOnlyList<PropertyInfo> properties)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            var parameter = parameters[i];
            var property = properties[i];
            if (!NameMatches(parameter.Name, property.Name) ||
                property.PropertyType != parameter.ParameterType)
            {
                return false;
            }
        }

        return true;
    }

    private static PropertyInfo[]? ReorderedConstructorProperties(
        IReadOnlyList<ParameterInfo> parameters,
        IReadOnlyList<PropertyInfo> properties)
    {
        var selected = new PropertyInfo[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var property = FindProperty(properties, parameter);
            if (property is null)
            {
                return null;
            }

            selected[i] = property;
        }

        return selected;
    }

    private static PropertyInfo? FindProperty(IReadOnlyList<PropertyInfo> properties, ParameterInfo parameter)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            if (NameMatches(parameter.Name, property.Name) &&
                property.PropertyType == parameter.ParameterType)
            {
                return property;
            }
        }

        return null;
    }

    private static bool NameMatches(string? parameterName, string propertyName)
        => string.Equals(parameterName, propertyName, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e)
    {
        var values = new SandboxValue[_properties.Length];
        CopySandboxValues(e, values, 0);
        return values;
    }

    public SandboxValue ToSandboxValue(TEvent e, int index)
        => _properties[index].ToSandboxValue(e);

    public void CopySandboxValues(TEvent e, SandboxValue[] destination, int destinationIndex)
    {
        for (var i = 0; i < _properties.Length; i++)
        {
            destination[destinationIndex + i] = _properties[i].ToSandboxValue(e);
        }
    }

    private static Func<TEvent, object?> CreateGetter(PropertyInfo property)
    {
        var instance = System.Linq.Expressions.Expression.Parameter(typeof(TEvent), "e");
        var propertyAccess = System.Linq.Expressions.Expression.Property(instance, property);
        var convert = System.Linq.Expressions.Expression.Convert(propertyAccess, typeof(object));
        return System.Linq.Expressions.Expression.Lambda<Func<TEvent, object?>>(convert, instance).Compile();
    }

    private static string EventParameterName(string propertyName)
        => PluginManifestNames.EventParameters.Prefix + propertyName;
}

internal readonly record struct ConventionEventProperty<TEvent>(
    string SettingType,
    Func<TEvent, object?> Getter)
{
    public SandboxValue ToSandboxValue(TEvent e)
        => LiveSettingTypeConverter.ToSandboxValue(SettingType, Getter(e));
}
