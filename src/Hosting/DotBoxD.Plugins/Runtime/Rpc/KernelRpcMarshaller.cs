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
                ? SandboxValue.FromInt64(EnumToInt64(value, type))
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
                    items[index++] = MarshalChild(item, elementType, "List element");
                }

                return SandboxValue.FromOwnedList(items, itemType);
            }

            var values = new List<SandboxValue>();
            foreach (var item in enumerable)
            {
                values.Add(MarshalChild(item, elementType, "List element"));
            }

            return SandboxValue.FromOwnedList(values.ToArray(), itemType);
        }

        if (MapTypes(type) is { } mapTypes)
        {
            if (value is not IDictionary dictionary)
            {
                throw new ArgumentException(
                    $"Kernel RPC service expected '{type}' to be a dictionary.",
                    nameof(value));
            }

            var keyType = SandboxTypeOf(mapTypes.Key);
            // Mirror the SandboxTypeOf map-key guard: the kernel verifier only accepts a fixed set of scalar map
            // keys (bool/int/long/string/opaque-id, not Guid or double). Reject an unsupported key here with a
            // catchable NotSupportedException instead of producing a Map<Guid,V> that later fails IsKnown at install.
            if (!keyType.IsValidMapKey())
            {
                throw new NotSupportedException(
                    $"Kernel RPC service map key type '{mapTypes.Key}' is not a supported sandbox map key.");
            }

            var valueType = SandboxTypeOf(mapTypes.Value);
            var entries = new Dictionary<SandboxValue, SandboxValue>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = MarshalChild(entry.Key, mapTypes.Key, "Map key");
                entries[key] = MarshalChild(entry.Value, mapTypes.Value, "Map value");
            }

            return SandboxValue.FromMap(entries, keyType, valueType);
        }

        if (IsDto(type))
        {
            var fields = GetRecordShape(type).Fields;
            var values = new SandboxValue[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                values[i] = MarshalChild(
                    GetRecordMemberValue(fields[i], value), RecordMemberType(fields[i]), $"DTO field '{fields[i].Name}'");
            }

            return SandboxValue.FromOwnedRecord(values);
        }

        throw new NotSupportedException($"Server extension cannot marshal type '{type}' to a sandbox value.");
    }

    // A nested child (list element, map key/value, or DTO field) of a marshaller-eligible value. The sandbox
    // value model has no null, so a null child is rejected with a clear, contextual NotSupportedException rather
    // than the bare ArgumentNullException ToSandboxValue would otherwise throw with only the parameter name.
    private static SandboxValue MarshalChild(object? value, Type type, string context)
    {
        if (value is null)
        {
            throw new NotSupportedException(
                $"{context} of type '{type}' was null; the sandbox value model has no null.");
        }

        return ToSandboxValue(value, type);
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

        if (MapTypes(type) is { } mapTypes && value is MapValue map)
        {
            var result = CreateDictionary(mapTypes.Key, mapTypes.Value);
            foreach (var pair in map.Values)
            {
                var key = FromSandboxValue(pair.Key, mapTypes.Key)
                    ?? throw new NotSupportedException("Server extension cannot marshal a null map key.");
                result[key] = FromSandboxValue(pair.Value, mapTypes.Value);
            }

            return result;
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
                arguments[i] = FromSandboxValue(record.Fields[i], RecordMemberType(fields[i]));
            }

            return shape.Construct(arguments);
        }

        throw new NotSupportedException($"Server extension cannot marshal a sandbox value to type '{type}'.");
    }

    public static SandboxType SandboxTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SandboxTypeOf(type, 0);
    }

    // The list/map/record nesting depth is bounded so a self-referential DTO (e.g. a class with a property of
    // its own type) fails with a catchable NotSupportedException instead of an uncatchable StackOverflowException
    // when, say, ConventionEventAdapter is constructed for it. Kept at or below the kernel verifier's structural
    // depth limit (SandboxType.IsKnown defaults to maxDepth 8) so a produced type is never rejected at install.
    private const int MaxTypeNestingDepth = 8;

    private static SandboxType SandboxTypeOf(Type type, int depth)
    {
        RejectNullableValueType(type);

        if (type == typeof(bool)) return SandboxType.Bool;
        if (type == typeof(int)) return SandboxType.I32;
        if (type == typeof(long)) return SandboxType.I64;
        if (type == typeof(double)) return SandboxType.F64;
        // float widens losslessly to the sandbox's only floating kind (F64); decode narrows back exactly.
        if (type == typeof(float)) return SandboxType.F64;
        if (type == typeof(string)) return SandboxType.String;
        if (type == typeof(Guid)) return SandboxType.Guid;
        if (type.IsEnum) return EnumUsesI64(type) ? SandboxType.I64 : SandboxType.I32;

        if (depth >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }

        if (ElementType(type) is { } elementType) return SandboxType.List(SandboxTypeOf(elementType, depth + 1));
        if (MapTypes(type) is { } mapTypes)
        {
            var keyType = SandboxTypeOf(mapTypes.Key, depth + 1);
            // The kernel verifier only accepts a fixed set of scalar map keys (bool/int/long/string/opaque-id, not
            // Guid or double). Reject an unsupported key here with a catchable NotSupportedException instead of
            // producing a Map<Guid,V> that later fails IsKnown validation at install.
            if (!keyType.IsValidMapKey())
            {
                throw new NotSupportedException(
                    $"Kernel RPC service map key type '{mapTypes.Key}' is not a supported sandbox map key.");
            }

            return SandboxType.Map(keyType, SandboxTypeOf(mapTypes.Value, depth + 1));
        }

        if (IsDto(type))
        {
            var fields = GetRecordShape(type).Fields;
            var fieldTypes = new SandboxType[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                fieldTypes[i] = SandboxTypeOf(RecordMemberType(fields[i]), depth + 1);
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
            var t when t == typeof(float) => SandboxValue.FromDouble((float)value!),
            var t when t == typeof(string) => SandboxValue.FromString((string)value!),
            var t when t == typeof(Guid) => SandboxValue.FromGuid((Guid)value!),
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
            (var t, F64Value d) when t == typeof(float) => (float)d.Value,
            (var t, StringValue s) when t == typeof(string) => s.Value,
            (var t, GuidValue g) when t == typeof(Guid) => g.Value,
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

    // A ulong-backed enum value above long.MaxValue overflows a range-checked Convert.ToInt64; reinterpret its
    // bits instead so the value carries losslessly (decode uses Enum.ToObject, which is also bit-preserving).
    private static long EnumToInt64(object value, Type type)
        => Enum.GetUnderlyingType(type) == typeof(ulong)
            ? unchecked((long)Convert.ToUInt64(value))
            : Convert.ToInt64(value);
}
