namespace SafeIR.Interpreter;

using SafeIR;

internal static class CollectionOperations
{
    public static SandboxValue CreateList(SandboxType itemType, SandboxContext context)
    {
        context.ChargeAllocation(8);
        return SandboxValue.FromList([], itemType);
    }

    public static SandboxValue BuildList(IReadOnlyList<SandboxValue> values, SandboxContext context)
    {
        context.ChargeAllocation(values.Count * 16);
        return SandboxValue.FromList(values);
    }

    public static SandboxValue CountList(SandboxValue list)
        => SandboxValue.FromInt32(AsList(list).Values.Count);

    public static SandboxValue GetListItem(SandboxValue index, SandboxValue list)
    {
        var values = AsList(list).Values;
        var i = AsI32(index).Value;
        if (i < 0 || i >= values.Count) {
            throw Error(SandboxErrorCode.InvalidInput, "list index is out of range");
        }

        return values[i];
    }

    public static SandboxValue AddListItem(SandboxValue item, SandboxValue list, SandboxContext context)
    {
        var source = AsList(list);
        RequireType(item, source.ItemType, "list item type mismatch");
        var values = source.Values.ToList();
        values.Add(item);
        context.ChargeAllocation(values.Count * 16);
        return SandboxValue.FromList(values, source.ItemType);
    }

    public static SandboxValue CreateMap(SandboxType mapType, SandboxContext context)
    {
        if (mapType.Name != "Map" || mapType.Arguments.Count != 2) {
            throw Error(SandboxErrorCode.ValidationError, "map.empty requires Map<K,V> type");
        }

        context.ChargeAllocation(16);
        return SandboxValue.FromMap(new Dictionary<SandboxValue, SandboxValue>(), mapType.Arguments[0], mapType.Arguments[1]);
    }

    public static SandboxValue ContainsMapKey(SandboxValue key, SandboxValue map)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        return SandboxValue.FromBool(typedMap.Values.ContainsKey(key));
    }

    public static SandboxValue GetMapValue(SandboxValue key, SandboxValue map)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        if (!typedMap.Values.TryGetValue(key, out var value)) {
            throw Error(SandboxErrorCode.NotFound, "map key was not found");
        }

        return value;
    }

    public static SandboxValue SetMapValue(SandboxValue value, SandboxValue key, SandboxValue map, SandboxContext context)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        RequireType(value, typedMap.ValueType, "map value type mismatch");
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values) {
            [key] = value
        };
        context.ChargeAllocation(Math.Max(1, values.Count) * 32);
        return SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType);
    }

    public static SandboxValue RemoveMapValue(SandboxValue key, SandboxValue map, SandboxContext context)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values);
        values.Remove(key);
        context.ChargeAllocation(Math.Max(1, values.Count) * 32);
        return SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType);
    }

    private static ListValue AsList(SandboxValue value)
        => value as ListValue ?? throw Error(SandboxErrorCode.InvalidInput, "expected list value");

    private static MapValue AsMap(SandboxValue value)
        => value as MapValue ?? throw Error(SandboxErrorCode.InvalidInput, "expected map value");

    private static I32Value AsI32(SandboxValue value)
        => value as I32Value ?? throw Error(SandboxErrorCode.InvalidInput, "expected I32 value");

    private static void RequireType(SandboxValue value, SandboxType expected, string message)
    {
        if (value.Type != expected) {
            throw Error(SandboxErrorCode.InvalidInput, message);
        }
    }

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message)
        => new(new SandboxError(code, message));
}
