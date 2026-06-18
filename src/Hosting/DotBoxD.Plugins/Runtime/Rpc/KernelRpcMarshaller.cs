using System.Collections;
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
public static partial class KernelRpcMarshaller
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

            var itemType = SandboxTypeOf(elementType);
            if (value is ICollection collection)
            {
                var items = new SandboxValue[collection.Count];
                var index = 0;
                foreach (var item in enumerable)
                {
                    items[index++] = ToSandboxValue(item, elementType);
                }

                return SandboxValue.FromOwnedList(items, itemType);
            }

            var values = new List<SandboxValue>();
            foreach (var item in enumerable)
            {
                values.Add(ToSandboxValue(item, elementType));
            }

            return SandboxValue.FromOwnedList(values.ToArray(), itemType);
        }

        if (IsDto(type))
        {
            var fields = GetRecordShape(type).Fields;
            var values = new SandboxValue[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                values[i] = ToSandboxValue(fields[i].GetValue(value), fields[i].PropertyType);
            }

            return SandboxValue.FromOwnedRecord(values);
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
            if (type.IsArray)
            {
                return ToArray(list.Values, elementType);
            }

            var resultList = CreateList(elementType);
            foreach (var item in list.Values)
            {
                resultList.Add(FromSandboxValue(item, elementType));
            }

            return resultList;
        }

        if (IsDto(type) && value is RecordValue record)
        {
            var shape = GetRecordShape(type);
            var fields = shape.Fields;
            if (record.Fields.Count != fields.Count)
            {
                throw new NotSupportedException($"Server extension record has {record.Fields.Count} fields but '{type}' expects {fields.Count}.");
            }

            var arguments = new object?[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                arguments[i] = FromSandboxValue(record.Fields[i], fields[i].PropertyType);
            }

            return shape.Construct(arguments);
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
            var fields = GetRecordShape(type).Fields;
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

    private static Array ToArray(IReadOnlyList<SandboxValue> values, Type elementType)
    {
        var array = Array.CreateInstance(elementType, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            array.SetValue(FromSandboxValue(values[i], elementType), i);
        }

        return array;
    }

    // An enum marshals through its underlying integer; widths that overflow I32 (uint/long/ulong) use I64.
    private static bool EnumUsesI64(Type type)
    {
        var underlying = Enum.GetUnderlyingType(type);
        return underlying == typeof(uint) || underlying == typeof(long) || underlying == typeof(ulong);
    }
}
