namespace SafeIR.Interpreter;

using SafeIR;

internal static class CollectionOperations
{
    public static SandboxValue CreateList(SandboxType itemType, SandboxContext context)
    {
        context.ChargeFuel(SandboxCollectionFuel.Empty());
        context.ChargeAllocation(8);
        return Charge(context, SandboxValue.FromList([], itemType));
    }

    public static SandboxValue BuildList(IReadOnlyList<SandboxValue> values, SandboxContext context)
    {
        context.ChargeFuel(SandboxCollectionFuel.Copy(values.Count));
        context.ChargeAllocation(values.Count * 16);
        return Charge(context, SandboxValue.FromList(values));
    }

    public static SandboxValue CountList(SandboxValue list, SandboxContext context)
    {
        var values = AsList(list).Values;
        context.ChargeFuel(SandboxCollectionFuel.Read(values.Count));
        return SandboxValue.FromInt32(values.Count);
    }

    public static SandboxValue GetListItem(SandboxValue index, SandboxValue list, SandboxContext context)
    {
        var values = AsList(list).Values;
        context.ChargeFuel(SandboxCollectionFuel.Read(values.Count));
        var i = AsI32(index).Value;
        if (i < 0 || i >= values.Count)
        {
            throw Error(SandboxErrorCode.InvalidInput, "list index is out of range");
        }

        return values[i];
    }

    public static SandboxValue AddListItem(SandboxValue item, SandboxValue list, SandboxContext context)
    {
        var source = AsList(list);
        RequireType(item, source.ItemType, "list item type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(source.Values.Count, addedCount: 1));
        var values = source.Values.ToList();
        values.Add(item);
        context.ChargeAllocation(values.Count * 16);
        return Charge(context, SandboxValue.FromList(values, source.ItemType));
    }

    public static SandboxValue CreateMap(SandboxType mapType, SandboxContext context)
    {
        if (mapType.Name != "Map" || mapType.Arguments.Count != 2)
        {
            throw Error(SandboxErrorCode.ValidationError, "map.empty requires Map<K,V> type");
        }

        context.ChargeFuel(SandboxCollectionFuel.Empty());
        context.ChargeAllocation(16);
        return Charge(
            context,
            SandboxValue.FromMap(new Dictionary<SandboxValue, SandboxValue>(), mapType.Arguments[0], mapType.Arguments[1]));
    }

    public static SandboxValue ContainsMapKey(SandboxValue key, SandboxValue map, SandboxContext context)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Read(typedMap.Values.Count));
        return SandboxValue.FromBool(typedMap.Values.ContainsKey(key));
    }

    public static SandboxValue GetMapValue(SandboxValue key, SandboxValue map, SandboxContext context)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Read(typedMap.Values.Count));
        if (!typedMap.Values.TryGetValue(key, out var value))
        {
            throw Error(SandboxErrorCode.NotFound, "map key was not found");
        }

        return value;
    }

    public static SandboxValue SetMapValue(SandboxValue value, SandboxValue key, SandboxValue map, SandboxContext context)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        RequireType(value, typedMap.ValueType, "map value type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count, addedCount: 1));
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values)
        {
            [key] = value
        };
        context.ChargeAllocation(Math.Max(1, values.Count) * 32);
        return Charge(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    public static SandboxValue RemoveMapValue(SandboxValue key, SandboxValue map, SandboxContext context)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count));
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values);
        values.Remove(key);
        context.ChargeAllocation(Math.Max(1, values.Count) * 32);
        return Charge(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    private static ListValue AsList(SandboxValue value)
    {
        var list = value as ListValue ?? throw Error(SandboxErrorCode.InvalidInput, "expected list value");
        SandboxValueValidator.RequireType(list, list.Type, "list item type mismatch");
        return list;
    }

    private static MapValue AsMap(SandboxValue value)
    {
        var map = value as MapValue ?? throw Error(SandboxErrorCode.InvalidInput, "expected map value");
        SandboxValueValidator.RequireType(map, map.Type, "map entry type mismatch");
        return map;
    }

    private static I32Value AsI32(SandboxValue value)
        => value as I32Value ?? throw Error(SandboxErrorCode.InvalidInput, "expected I32 value");

    private static void RequireType(SandboxValue value, SandboxType expected, string message)
        => SandboxValueValidator.RequireType(value, expected, message);

    private static SandboxValue Charge(SandboxContext context, SandboxValue value)
    {
        context.ChargeValue(value);
        return value;
    }

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message)
        => new(new SandboxError(code, message));
}
