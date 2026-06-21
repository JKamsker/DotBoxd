using System.Collections;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    internal static object? FromKernelRpcValue(KernelRpcValue value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        RejectNullableValueType(type);

        if (TryScalarFromKernel(value, type, out var scalar))
        {
            return scalar;
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(
                type,
                EnumUsesI64(type) ? value.Int64Value : value.Int32Value);
        }

        if (value.Kind == KernelRpcValueKind.Record && DtoShape(type) is { } shape)
        {
            if (value.ItemCount != shape.Fields.Count)
            {
                throw new NotSupportedException(
                    $"Server extension record has {value.ItemCount} fields but '{type}' expects {shape.Fields.Count}.");
            }

            return shape.Construct(value);
        }

        if (ElementType(type) is { } elementType)
        {
            value.RequireKind(KernelRpcValueKind.List);
            return type.IsArray
                ? ToArray(value.ItemSpan, elementType)
                : ToList(value.ItemSpan, elementType);
        }

        if (MapTypes(type) is { } mapTypes)
        {
            value.RequireKind(KernelRpcValueKind.Map);
            return ToDictionary(value.ItemSpan, mapTypes.Key, mapTypes.Value);
        }

        throw new NotSupportedException($"Server extension cannot marshal a kernel RPC value to type '{type}'.");
    }

    private static bool TryScalarFromKernel(KernelRpcValue value, Type type, out object? result)
    {
        result = type switch
        {
            var t when t == typeof(bool) => value.BoolValue,
            var t when t == typeof(int) => value.Int32Value,
            var t when t == typeof(long) => value.Int64Value,
            var t when t == typeof(float) => (float)value.DoubleValue,
            var t when t == typeof(double) => value.DoubleValue,
            var t when t == typeof(string) => value.TextValue,
            var t when t == typeof(Guid) => value.GuidValue,
            _ => null
        };
        return result is not null;
    }

    private static Array ToArray(ReadOnlySpan<KernelRpcValue> values, Type elementType)
    {
        var array = Array.CreateInstance(elementType, values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            array.SetValue(FromKernelRpcValue(values[i], elementType), i);
        }

        return array;
    }

    private static IList ToList(ReadOnlySpan<KernelRpcValue> values, Type elementType)
    {
        var result = CreateList(elementType);
        for (var i = 0; i < values.Length; i++)
        {
            result.Add(FromKernelRpcValue(values[i], elementType));
        }

        return result;
    }

    private static IDictionary ToDictionary(ReadOnlySpan<KernelRpcValue> values, Type keyType, Type valueType)
    {
        if ((values.Length & 1) != 0)
        {
            throw new FormatException("Server extension map payload has an odd key/value entry count.");
        }

        var result = CreateDictionary(keyType, valueType);
        for (var i = 0; i < values.Length; i += 2)
        {
            var key = FromKernelRpcValue(values[i], keyType)
                ?? throw new NotSupportedException("Server extension cannot marshal a null map key.");
            result[key] = FromKernelRpcValue(values[i + 1], valueType);
        }

        return result;
    }
}
