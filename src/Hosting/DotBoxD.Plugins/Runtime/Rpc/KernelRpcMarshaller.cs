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
        if (TryNullableToSandboxValue(value, type, out var nullable))
        {
            return nullable;
        }

        ArgumentNullException.ThrowIfNull(value);
        if (TryScalarToSandbox(value, type) is { } scalar)
        {
            return scalar;
        }

        if (TryDateTimeToSandboxValue(value, type, out var dateTime))
        {
            return dateTime;
        }

        if (TryFrameworkStructToSandboxValue(value, type, out var frameworkStruct))
        {
            return frameworkStruct;
        }

        if (type.IsEnum)
        {
            return EnumUsesI64(type)
                ? SandboxValue.FromInt64(EnumToInt64(value, type))
                : SandboxValue.FromInt32(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
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
            if (value is not IEnumerable enumerable)
            {
                throw new ArgumentException(
                    $"Kernel RPC service expected '{type}' to be a dictionary.",
                    nameof(value));
            }

            RejectUnsupportedMapKeyType(mapTypes.Key);
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
            var entries = new MapValueBuilder(value is ICollection collection ? collection.Count : 0);
            foreach (var entry in MapEntries(enumerable, mapTypes.Key, mapTypes.Value))
            {
                var key = MarshalChild(entry.Key, mapTypes.Key, "Map key");
                entries.Set(key, MarshalChild(entry.Value, mapTypes.Value, "Map value"));
            }

            return SandboxValue.FromOwnedMap(entries, keyType, valueType);
        }

        if (DtoShape(type) is { } shape)
        {
            var fields = shape.Fields;
            var values = new SandboxValue[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                values[i] = MarshalChild(shape.GetValue(value, i), fields[i].Type, $"DTO field '{fields[i].Name}'");
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
        if (value is null && Nullable.GetUnderlyingType(type) is null)
        {
            throw new NotSupportedException(
                $"{context} of type '{type}' was null; the sandbox value model has no null.");
        }

        return ToSandboxValue(value, type);
    }

    public static object? FromSandboxValue(SandboxValue value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (TryNullableFromSandboxValue(value, type, out var nullable))
        {
            return nullable;
        }

        if (TryScalarFromSandbox(value, type, out var scalar))
        {
            return scalar;
        }

        if (TryDateTimeFromSandboxValue(value, type, out var dateTime))
        {
            return dateTime;
        }

        if (TryFrameworkStructFromSandboxValue(value, type, out var frameworkStruct))
        {
            return frameworkStruct;
        }

        if (type.IsEnum)
        {
            if (EnumUsesI64(type))
            {
                return value is I64Value longValue
                    ? EnumFromInt64(type, longValue.Value)
                    : throw CannotMarshalEnum(value, type, SandboxType.I64);
            }

            return value is I32Value intValue
                ? EnumFromInt32(type, intValue.Value)
                : throw CannotMarshalEnum(value, type, SandboxType.I32);
        }

        if (value is RecordValue record && DtoShape(type) is { } shape)
        {
            var fields = shape.Fields;
            if (record.Fields.Count != fields.Count)
            {
                throw new NotSupportedException($"Server extension record has {record.Fields.Count} fields but '{type}' expects {fields.Count}.");
            }

            return shape.Construct(record);
        }

        if (ElementType(type) is { } elementType && value is ListValue list)
        {
            if (type.IsArray)
            {
                return ToArray(list.Values, elementType);
            }

            var resultList = CreateList(elementType, list.Values.Count);
            foreach (var item in list.Values)
            {
                resultList.Add(FromSandboxValue(item, elementType));
            }

            return CompleteList(type, elementType, resultList);
        }

        if (MapTypes(type) is { } mapTypes && value is MapValue map)
        {
            RejectUnsupportedMapKeyType(mapTypes.Key);
            var result = CreateDictionary(mapTypes.Key, mapTypes.Value, map.Values.Count);
            foreach (var pair in map.Entries)
            {
                var key = FromSandboxValue(pair.Key, mapTypes.Key)
                    ?? throw new NotSupportedException("Server extension cannot marshal a null map key.");
                result[key] = FromSandboxValue(pair.Value, mapTypes.Value);
            }

            return CompleteDictionary(type, mapTypes.Key, mapTypes.Value, result);
        }

        throw new NotSupportedException($"Server extension cannot marshal a sandbox value to type '{type}'.");
    }

    private static NotSupportedException CannotMarshalEnum(
        SandboxValue value,
        Type type,
        SandboxType expected)
        => new($"Server extension cannot marshal sandbox value '{value.Type}' to enum '{type}'; expected '{expected}'.");
}
