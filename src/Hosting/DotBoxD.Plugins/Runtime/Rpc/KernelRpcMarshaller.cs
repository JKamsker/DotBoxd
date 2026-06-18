using System.Collections;
using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime.Rpc;

/// <summary>
/// Marshals between plain C# values and the sandbox <see cref="SandboxValue"/> world for server extension
/// service calls: caller arguments are converted to sandbox values for
/// <see cref="InstalledKernel.InvokeServerExtensionAsync"/>, and the returned value is converted back to the
/// declared C# result type. Supports the supported scalars, <c>List&lt;T&gt;</c>/<c>IEnumerable&lt;T&gt;</c>,
/// and DTOs (records/structs/classes) mapped to positional records by their fields' declaration order —
/// the same order the analyzer used when it lowered the kernel, so fields line up by position.
/// </summary>
public static class KernelRpcMarshaller
{
    public static SandboxValue ToSandboxValue(object? value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(value);
        RejectNullableValueType(type);

        if (TryScalarToSandbox(value, type) is { } scalar)
        {
            return scalar;
        }

        if (type.IsEnum)
        {
            return EnumUsesI64(type)
                ? SandboxValue.FromInt64(Convert.ToInt64(value))
                : SandboxValue.FromInt32(Convert.ToInt32(value));
        }

        if (ElementType(type) is { } elementType)
        {
            if (value is not IEnumerable enumerable)
            {
                throw new ArgumentException(
                    $"Kernel RPC service expected '{type}' to be enumerable.",
                    nameof(value));
            }

            var items = new List<SandboxValue>();
            foreach (var item in enumerable)
            {
                items.Add(ToSandboxValue(item, elementType));
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

        throw new NotSupportedException($"Server extension cannot marshal type '{type}' to a sandbox value.");
    }

    public static object? FromSandboxValue(SandboxValue value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        RejectNullableValueType(type);

        if (TryScalarFromSandbox(value, type, out var scalar))
        {
            return scalar;
        }

        if (type.IsEnum)
        {
            return value switch
            {
                I32Value i => Enum.ToObject(type, i.Value),
                I64Value l => Enum.ToObject(type, l.Value),
                _ => throw new NotSupportedException(
                    $"Server extension cannot marshal a sandbox value to enum '{type}'.")
            };
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
                throw new NotSupportedException($"Server extension record has {record.Fields.Count} fields but '{type}' expects {fields.Count}.");
            }

            var arguments = new object?[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                arguments[i] = FromSandboxValue(record.Fields[i], fields[i].PropertyType);
            }

            return Construct(type, fields, arguments);
        }

        throw new NotSupportedException($"Server extension cannot marshal a sandbox value to type '{type}'.");
    }

    public static SandboxType SandboxTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        RejectNullableValueType(type);

        if (type == typeof(bool)) return SandboxType.Bool;
        if (type == typeof(int)) return SandboxType.I32;
        if (type == typeof(long)) return SandboxType.I64;
        if (type == typeof(double)) return SandboxType.F64;
        if (type == typeof(string)) return SandboxType.String;
        if (type.IsEnum) return EnumUsesI64(type) ? SandboxType.I64 : SandboxType.I32;
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

        throw new NotSupportedException($"Server extension has no sandbox type for '{type}'.");
    }

    private static SandboxValue? TryScalarToSandbox(object? value, Type type)
        => type switch
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
        result = (type, value) switch
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

    private static void RejectNullableValueType(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is null)
        {
            return;
        }

        throw new NotSupportedException(
            $"Kernel RPC service nullable type '{type}' is not supported.");
    }

    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1)
            {
                throw new NotSupportedException(
                    $"Kernel RPC service cannot marshal multidimensional array type '{type}'.");
            }

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

    // An enum marshals through its underlying integer; widths that overflow I32 (uint/long/ulong) use I64.
    private static bool EnumUsesI64(Type type)
    {
        var underlying = Enum.GetUnderlyingType(type);
        return underlying == typeof(uint) || underlying == typeof(long) || underlying == typeof(ulong);
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
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var property in type.GetProperties(flags))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0 &&
                !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            {
                properties.Add(property);
            }
        }

        properties.Sort(static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
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
            var assigned = new bool[parameters.Length];
            var matched = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = FieldIndex(fields, parameters[i].Name);
                if (fieldIndex < 0 ||
                    assigned[fieldIndex] ||
                    parameters[i].ParameterType != fields[fieldIndex].PropertyType)
                {
                    matched = false;
                    break;
                }

                ordered[i] = arguments[fieldIndex];
                assigned[fieldIndex] = true;
            }

            if (matched)
            {
                return constructor.Invoke(ordered);
            }
        }

        var instance = Activator.CreateInstance(type)
            ?? throw new NotSupportedException($"Server extension could not construct '{type}'.");
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
            if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        var match = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match >= 0)
            {
                return -1;
            }

            match = i;
        }

        return match;
    }

    private static Array ToArray(IList list, Type elementType)
    {
        var array = Array.CreateInstance(elementType, list.Count);
        list.CopyTo(array, 0);
        return array;
    }
}
