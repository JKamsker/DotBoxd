using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Input;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime;

public sealed class PluginEventAdapterRegistry
{
    private readonly Dictionary<Type, RegisteredPluginEventAdapter> _adapters = [];

    public void Register<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        var parameters = adapter.Parameters;
        PluginEventValueWriterShapeValidator.Validate(adapter, parameters);
        var shape = new PluginEventShape(adapter.EventName, parameters);
        ValidateEventNameShape(typeof(TEvent), shape);
        _adapters[typeof(TEvent)] = new(adapter, shape);
    }

    public IPluginEventAdapter<TEvent> Resolve<TEvent>()
    {
        if (_adapters.TryGetValue(typeof(TEvent), out var registered))
        {
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
            // The manifest event name may be fully qualified (Namespace.TypeName) while an adapter reports
            // only the simple name; EventNameMatch bridges that seam and still honours exact matches.
            if (EventNameMatch.Matches(current.EventName, eventName))
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
        foreach (var type in typeof(TEvent).Assembly.GetTypes())
        {
            if (type.IsAbstract || !adapterType.IsAssignableFrom(type))
            {
                continue;
            }

            var instance = StaticInstance(type) ?? Activator.CreateInstance(type);
            return (IPluginEventAdapter<TEvent>)instance!;
        }

        return null;
    }

    private void ValidateEventNameShape(Type eventType, PluginEventShape shape)
    {
        foreach (var registered in _adapters)
        {
            if (registered.Key == eventType)
            {
                continue;
            }

            var current = registered.Value.Shape;
            if (!string.Equals(current.EventName, shape.EventName, StringComparison.Ordinal) ||
                PluginParameterShape.Matches(current.Parameters, shape.Parameters))
            {
                continue;
            }
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK034", $"Event adapter name '{shape.EventName}' is already registered with a different parameter shape.")
            ]);
        }
    }

    private static object? StaticInstance(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(p => string.Equals(p.Name, "Instance", StringComparison.Ordinal) &&
                                 type.IsAssignableFrom(p.PropertyType))
            ?.GetValue(null);
}

internal readonly record struct RegisteredPluginEventAdapter(object Adapter, PluginEventShape Shape);

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
            // Event properties marshal through KernelRpcMarshaller — the full marshaller-eligible set (scalars,
            // Guid, enums, lists/arrays, maps, and DTO records), not just the 5 live-setting scalars — so the
            // adapter's parameter shape and per-event values match the SandboxTypes the analyzer now emits for the
            // kernel (see SandboxTypeSourceEmitter). This is what lets a whole-event RunLocal push the entire
            // record (Guid id, enum, nested DTO, …) rather than only scalar events.
            _properties[i] = CreateEventProperty(property);
            parameters[i] = new Parameter(
                EventParameterName(property.Name),
                KernelRpcMarshaller.SandboxTypeOf(_properties[i].ValueType));
        }

        Parameters = parameters;
    }

    public string EventName => EventNameFor(typeof(TEvent));

    public IReadOnlyList<Parameter> Parameters { get; }

    public int EventValueCount => _properties.Length;

    private static string EventNameFor(Type eventType)
        => eventType.GetCustomAttribute<HookAttribute>(inherit: false)?.Name ?? eventType.Name;

    public static ConventionEventAdapter<TEvent> Create()
    {
        var properties = ReadableProperties(typeof(TEvent));
        return new ConventionEventAdapter<TEvent>(properties);
    }

    private static IReadOnlyList<PropertyInfo> ReadableProperties(Type eventType)
    {
        var properties = ReadablePropertiesInHierarchy(eventType).ToArray();
        ValidatePropertyNames(properties);

        // Declaration order (MetadataToken within each hierarchy level), the single wire-field order shared with
        // the analyzer's kernel parameters (PluginEventPropertyReader) and the decoder side
        // (KernelRpcMarshaller.GetRecordShape, which reconstructs via the constructor map). Reordering to
        // constructor-parameter order would only match positional records and would silently misalign the pushed
        // whole-event record from the decoder for a non-positional event class.
        return properties;
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
                // Skip [IgnoreDataMember] non-wire members (e.g. a lazily-resolved context snapshot): they are
                // not serialized data, so excluding them here keeps the pushed whole-event record's fields in
                // lockstep with the analyzer's kernel parameters and the decode-side GetRecordShape.
                if (property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0 &&
                    !KernelRpcMarshaller.IsIgnoredMember(property))
                {
                    yield return property;
                }
            }
        }
    }

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

    private static ConventionEventProperty<TEvent> CreateEventProperty(PropertyInfo property)
    {
        if (PolymorphicHandleRuntimeMetadataReader.TryResolve(property.PropertyType, out var handle))
        {
            return new ConventionEventProperty<TEvent>(
                handle.KeyType,
                CreateHandleKeyGetter(property, handle),
                CoerceNullStringToEmpty: false);
        }

        return new ConventionEventProperty<TEvent>(
            property.PropertyType,
            CreateGetter(property),
            CoerceNullStringToEmpty: true);
    }

    private static Func<TEvent, object?> CreateHandleKeyGetter(
        PropertyInfo property,
        PolymorphicHandleRuntimeMetadata handle)
    {
        var instance = System.Linq.Expressions.Expression.Parameter(typeof(TEvent), "e");
        var handleAccess = System.Linq.Expressions.Expression.Property(instance, property);
        var typedHandle = System.Linq.Expressions.Expression.Convert(handleAccess, handle.HandleType);
        var keyAccess = handle.KeyProperty is not null
            ? System.Linq.Expressions.Expression.Property(typedHandle, handle.KeyProperty)
            : System.Linq.Expressions.Expression.Field(typedHandle, handle.KeyField!);
        var keyAsObject = System.Linq.Expressions.Expression.Convert(keyAccess, typeof(object));
        System.Linq.Expressions.Expression body = property.PropertyType.IsValueType
            ? keyAsObject
            : System.Linq.Expressions.Expression.Condition(
                System.Linq.Expressions.Expression.Equal(
                    handleAccess,
                    System.Linq.Expressions.Expression.Constant(null, property.PropertyType)),
                System.Linq.Expressions.Expression.Constant(null, typeof(object)),
                keyAsObject);
        return System.Linq.Expressions.Expression.Lambda<Func<TEvent, object?>>(body, instance).Compile();
    }

    private static string EventParameterName(string propertyName)
        => PluginManifestNames.EventParameters.Prefix + propertyName;
}

internal readonly record struct ConventionEventProperty<TEvent>(
    Type ValueType,
    Func<TEvent, object?> Getter,
    bool CoerceNullStringToEmpty)
{
    public SandboxValue ToSandboxValue(TEvent e)
    {
        var value = Getter(e);

        // The sandbox/wire model has no null. Preserve the historical scalar behaviour of treating a null
        // string as empty; other null reference-typed properties fail with a clear message instead of the
        // marshaller's bare ArgumentNullException.
        if (value is null)
        {
            if (Nullable.GetUnderlyingType(ValueType) is not null)
            {
                return KernelRpcMarshaller.ToSandboxValue(null, ValueType);
            }

            return CoerceNullStringToEmpty && ValueType == typeof(string)
                ? SandboxValue.FromString(string.Empty)
                : throw new NotSupportedException(
                    $"Event property of type '{ValueType}' was null; the sandbox value model has no null.");
        }

        return KernelRpcMarshaller.ToSandboxValue(value, ValueType);
    }
}
