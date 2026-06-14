namespace SafeIR.Plugins;

using System.Collections;
using System.Reflection;
using SafeIR;

/// <summary>
/// Marshals between plain C# values and the sandbox <see cref="SandboxValue"/> world for kernel RPC
/// service calls: caller arguments are converted to sandbox values for
/// <see cref="InstalledKernel.InvokeRpcAsync"/>, and the returned value is converted back to the
/// declared C# result type. Supports the supported scalars, <c>List&lt;T&gt;</c>/<c>IEnumerable&lt;T&gt;</c>,
/// and DTOs (records/structs/classes) mapped to positional records by their fields' declaration order —
/// the same order the analyzer used when it lowered the kernel, so fields line up by position.
/// </summary>
public static class KernelRpcMarshaller
{
    public static SandboxValue ToSandboxValue(object? value, Type type)
    {
        if (TryScalarToSandbox(value, type) is { } scalar)
        {
            return scalar;
        }

        if (ElementType(type) is { } elementType)
        {
            var items = new List<SandboxValue>();
            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    items.Add(ToSandboxValue(item, elementType));
                }
            }

            return SandboxValue.FromList(items, SandboxTypeOf(elementType));
        }

        if (IsDto(type))
        {
            var fields = RecordFields(type);
            var values = new SandboxValue[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                values[i] = ToSandboxValue(fields[i].GetValue(value), fields[i].PropertyType);
            }

            return SandboxValue.FromRecord(values);
        }

        throw new NotSupportedException($"Kernel RPC service cannot marshal type '{type}' to a sandbox value.");
    }

    public static object? FromSandboxValue(SandboxValue value, Type type)
    {
        if (TryScalarFromSandbox(value, type, out var scalar))
        {
            return scalar;
        }

        if (ElementType(type) is { } elementType && value is ListValue list)
        {
            var resultList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            foreach (var item in list.Values)
            {
                resultList.Add(FromSandboxValue(item, elementType));
            }

            return type.IsArray ? ToArray(resultList, elementType) : resultList;
        }

        if (IsDto(type) && value is RecordValue record)
        {
            var fields = RecordFields(type);
            if (record.Fields.Count != fields.Count)
            {
                throw new NotSupportedException($"Kernel RPC service record has {record.Fields.Count} fields but '{type}' expects {fields.Count}.");
            }

            var arguments = new object?[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                arguments[i] = FromSandboxValue(record.Fields[i], fields[i].PropertyType);
            }

            return Construct(type, fields, arguments);
        }

        throw new NotSupportedException($"Kernel RPC service cannot marshal a sandbox value to type '{type}'.");
    }

    public static SandboxType SandboxTypeOf(Type type)
    {
        if (type == typeof(bool)) return SandboxType.Bool;
        if (type == typeof(int)) return SandboxType.I32;
        if (type == typeof(long)) return SandboxType.I64;
        if (type == typeof(double)) return SandboxType.F64;
        if (type == typeof(string)) return SandboxType.String;
        if (ElementType(type) is { } elementType) return SandboxType.List(SandboxTypeOf(elementType));
        if (IsDto(type))
        {
            var fields = RecordFields(type);
            var fieldTypes = new SandboxType[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                fieldTypes[i] = SandboxTypeOf(fields[i].PropertyType);
            }

            return SandboxType.Record(fieldTypes);
        }

        throw new NotSupportedException($"Kernel RPC service has no sandbox type for '{type}'.");
    }

    private static SandboxValue? TryScalarToSandbox(object? value, Type type)
        => Unwrap(type) switch
        {
            var t when t == typeof(bool) => SandboxValue.FromBool((bool)value!),
            var t when t == typeof(int) => SandboxValue.FromInt32((int)value!),
            var t when t == typeof(long) => SandboxValue.FromInt64((long)value!),
            var t when t == typeof(double) => SandboxValue.FromDouble((double)value!),
            var t when t == typeof(string) => SandboxValue.FromString((string)value!),
            _ => null
        };

    private static bool TryScalarFromSandbox(SandboxValue value, Type type, out object? result)
    {
        result = (Unwrap(type), value) switch
        {
            (var t, BoolValue b) when t == typeof(bool) => b.Value,
            (var t, I32Value i) when t == typeof(int) => i.Value,
            (var t, I64Value l) when t == typeof(long) => l.Value,
            (var t, F64Value d) when t == typeof(double) => d.Value,
            (var t, StringValue s) when t == typeof(string) => s.Value,
            _ => null
        };
        return result is not null;
    }

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IList<>) || definition == typeof(IEnumerable<>) ||
                definition == typeof(IReadOnlyCollection<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static bool IsDto(Type type)
        => type != typeof(string) &&
           !type.IsPrimitive &&
           !type.IsEnum &&
           ElementType(type) is null &&
           (type.IsClass || type.IsValueType) &&
           RecordFields(type).Count > 0;

    private static IReadOnlyList<PropertyInfo> RecordFields(Type type)
    {
        var properties = new List<PropertyInfo>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0 &&
                !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            {
                properties.Add(property);
            }
        }

        // For positional records/structs the primary constructor parameter order is the canonical field
        // order the analyzer lowered against; align to it when a matching constructor exists.
        foreach (var constructor in type.GetConstructors())
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == properties.Count && parameters.Length > 0 &&
                Array.TrueForAll(parameters, p => properties.Exists(pr => NameMatches(pr.Name, p.Name))))
            {
                var ordered = new List<PropertyInfo>(parameters.Length);
                foreach (var parameter in parameters)
                {
                    ordered.Add(properties.Find(pr => NameMatches(pr.Name, parameter.Name))!);
                }

                return ordered;
            }
        }

        return properties;
    }

    private static object Construct(Type type, IReadOnlyList<PropertyInfo> fields, object?[] arguments)
    {
        // arguments[i] is the value for fields[i]; reorder to each candidate constructor's parameter order.
        foreach (var constructor in type.GetConstructors())
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length != fields.Count || parameters.Length == 0)
            {
                continue;
            }

            var ordered = new object?[parameters.Length];
            var matched = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = FieldIndex(fields, parameters[i].Name);
                if (fieldIndex < 0)
                {
                    matched = false;
                    break;
                }

                ordered[i] = arguments[fieldIndex];
            }

            if (matched)
            {
                return constructor.Invoke(ordered);
            }
        }

        var instance = Activator.CreateInstance(type)
            ?? throw new NotSupportedException($"Kernel RPC service could not construct '{type}'.");
        for (var i = 0; i < fields.Count; i++)
        {
            fields[i].SetValue(instance, arguments[i]);
        }

        return instance;
    }

    private static int FieldIndex(IReadOnlyList<PropertyInfo> fields, string? name)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (NameMatches(fields[i].Name, name))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool NameMatches(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static Array ToArray(IList list, Type elementType)
    {
        var array = Array.CreateInstance(elementType, list.Count);
        list.CopyTo(array, 0);
        return array;
    }
}
