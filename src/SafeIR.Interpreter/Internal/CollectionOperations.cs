namespace SafeIR.Interpreter.Internal;

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
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(values.Count, 16));
        return Charge(context, SandboxValue.FromList(values));
    }

    public static SandboxValue CountList(SandboxValue list, SandboxContext context)
    {
        var values = AsListReadOnly(list).Values;
        context.ChargeFuel(SandboxCollectionFuel.Read(values.Count));
        return SandboxValue.FromInt32(values.Count);
    }

    public static SandboxValue GetListItem(SandboxValue index, SandboxValue list, SandboxContext context)
    {
        var values = AsListReadOnly(list).Values;
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
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            source.Values.Count,
            addedCount: 1,
            bytesPerElement: 16));
        var values = source.Values.ToList();
        values.Add(item);
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
        var typedMap = AsMapReadOnly(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Read(typedMap.Values.Count));
        return SandboxValue.FromBool(typedMap.Values.ContainsKey(key));
    }

    public static SandboxValue GetMapValue(SandboxValue key, SandboxValue map, SandboxContext context)
    {
        var typedMap = AsMapReadOnly(map);
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
        var addedCount = typedMap.Values.ContainsKey(key) ? 0 : 1;
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            typedMap.Values.Count,
            addedCount,
            bytesPerElement: 32,
            minimumOne: true));
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values)
        {
            [key] = value
        };
        return Charge(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    public static SandboxValue RemoveMapValue(SandboxValue key, SandboxValue map, SandboxContext context)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count));
        var count = typedMap.Values.ContainsKey(key) ? typedMap.Values.Count - 1 : typedMap.Values.Count;
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(count, 32, minimumOne: true));
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values);
        values.Remove(key);
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

    // Read-only collection operations only need the runtime kind, not a recursive
    // re-walk of every element. Collection contents are already validated against
    // their declared element types at trust boundaries (entrypoint inputs via
    // EntrypointBinder and binding returns via ChargeBindingReturn) and stay typed
    // through every internal constructor, so reads can trust the snapshotted value.
    private static ListValue AsListReadOnly(SandboxValue value)
        => value as ListValue ?? throw Error(SandboxErrorCode.InvalidInput, "expected list value");

    private static MapValue AsMapReadOnly(SandboxValue value)
        => value as MapValue ?? throw Error(SandboxErrorCode.InvalidInput, "expected map value");

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
