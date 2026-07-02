using System.Collections;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    internal static object? FromKernelRpcValue(KernelRpcValue value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (TryNullableFromKernelRpcValue(value, type, out var nullable))
        {
            return nullable;
        }

        if (TryScalarFromKernel(value, type, out var scalar))
        {
            return scalar;
        }

        if (TryDateTimeFromKernelRpcValue(value, type, out var dateTime))
        {
            return dateTime;
        }

        if (TryDecimalFromKernelRpcValue(value, type, out var decimalValue))
        {
            return decimalValue;
        }

        if (TryFrameworkStructFromKernelRpcValue(value, type, out var frameworkStruct))
        {
            return frameworkStruct;
        }

        if (type.IsEnum)
        {
            return EnumUsesI64(type)
                ? EnumFromInt64(type, value.Int64Value)
                : EnumFromInt32(type, value.Int32Value);
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
            if (type.IsArray)
            {
                return ToArray(value.ItemSpan, elementType);
            }

            return CompleteList(type, elementType, ToList(value.ItemSpan, elementType));
        }

        if (MapTypes(type) is { } mapTypes)
        {
            value.RequireKind(KernelRpcValueKind.Map);
            RejectUnsupportedMapKeyType(mapTypes.Key);
            return CompleteDictionary(
                type,
                mapTypes.Key,
                mapTypes.Value,
                ToDictionary(value.ItemSpan, mapTypes.Key, mapTypes.Value));
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
            var t when t == typeof(float) => DoubleToSingle(value.DoubleValue),
            var t when t == typeof(double) => value.DoubleValue,
            var t when t == typeof(string) => value.TextValue,
            var t when t == typeof(Guid) => value.GuidValue,
            var t when t == typeof(DateOnly) => DateOnlyFromDayNumber(value.Int32Value),
            var t when t == typeof(TimeOnly) => TimeOnlyFromTicks(value.Int64Value),
            var t when t == typeof(TimeSpan) => new TimeSpan(value.Int64Value),
            var t when t == typeof(CancellationToken) => new CancellationToken(value.BoolValue),
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
        var result = CreateList(elementType, values.Length);
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

        var result = CreateDictionary(keyType, valueType, values.Length / 2);
        for (var i = 0; i < values.Length; i += 2)
        {
            var key = FromKernelRpcValue(values[i], keyType)
                ?? throw new NotSupportedException("Server extension cannot marshal a null map key.");
            if (result.Contains(key))
            {
                throw new FormatException("Server extension map payload contains a duplicate key.");
            }

            result.Add(key, FromKernelRpcValue(values[i + 1], valueType));
        }

        return result;
    }
}
