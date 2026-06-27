using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using SandboxContext = DotBoxD.Kernels.Sandbox.SandboxContext;

namespace DotBoxD.Kernels.Interpreter.Internal;

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

    public static SandboxValue BuildList(SandboxValue[] values, SandboxContext context)
    {
        context.ChargeFuel(SandboxCollectionFuel.Copy(values.Length));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(values.Length, 16));
        var itemType = values.Length == 0 ? SandboxType.Unit : values[0].Type;
        return Charge(context, SandboxValue.FromOwnedList(values, itemType));
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
        // The source list was already validated at its trust boundary and is immutable, so its elements are
        // trusted without a deep re-walk (same rationale as the read operations' AsListReadOnly). Only the
        // newly added item needs validation. Using the validating AsList here re-walked the whole list on
        // every add, making list.add O(n) per call and list-building O(n^2).
        var source = AsListReadOnly(list);
        RequireType(item, source.ItemType, "list item type mismatch");
        var count = source.Values.Count;
        context.ChargeFuel(SandboxCollectionFuel.Copy(count, addedCount: 1));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            count,
            addedCount: 1,
            bytesPerElement: 16));

        // Structural-sharing append (O(log n), no full copy) plus incremental shape charging (O(1)). Charged
        // fuel/allocation/shape are identical to the old copy+walk path; only the runtime data structure and
        // wall-time change, turning repeated list.add from O(n^2) into O(n log n) total.
        var appended = source.Append(item);
        ValueShapeCache.ChargeListAppend(context, source, item, appended, count + 1);
        return appended;
    }

    public static SandboxValue BuildRecord(IReadOnlyList<SandboxValue> fields, SandboxContext context)
    {
        context.ChargeFuel(SandboxCollectionFuel.Copy(fields.Count));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(fields.Count, 16));
        return Charge(context, SandboxValue.FromRecord(fields));
    }

    public static SandboxValue BuildRecord(SandboxValue[] fields, SandboxContext context)
    {
        context.ChargeFuel(SandboxCollectionFuel.Copy(fields.Length));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(fields.Length, 16));
        return Charge(context, SandboxValue.FromOwnedRecord(fields));
    }

    public static SandboxValue GetRecordField(SandboxValue index, SandboxValue record, SandboxContext context)
    {
        var fields = AsRecordReadOnly(record).Fields;
        context.ChargeFuel(SandboxCollectionFuel.Read(fields.Count));
        var i = AsI32(index).Value;
        if (i < 0 || i >= fields.Count)
        {
            throw Error(SandboxErrorCode.InvalidInput, "record field index is out of range");
        }

        return fields[i];
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
            SandboxValue.FromOwnedMap(new Dictionary<SandboxValue, SandboxValue>(), mapType.Arguments[0], mapType.Arguments[1]));
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
        // Source map is already validated and immutable, so trust its entries (as the read path does) and
        // validate only the new key/value; the validating AsMap re-walked the whole map on every set.
        var typedMap = AsMapReadOnly(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        RequireType(value, typedMap.ValueType, "map value type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count, addedCount: 1));
        var isReplace = typedMap.Values.ContainsKey(key);
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            typedMap.Values.Count,
            isReplace ? 0 : 1,
            bytesPerElement: 32,
            minimumOne: true));
        var updated = typedMap.SetEntry(key, value);

        // Replacing a zero-shape scalar entry keeps the aggregate map shape unchanged, so it can reuse the
        // source shape exactly. Complex replacements fall back to a full walk. Inserting a new key adds exactly
        // one entry, so charge it incrementally from the source's cached shape (same fuel/shape as a walk).
        if (isReplace)
        {
            return ValueShapeCache.TryChargeScalarMapReplace(context, typedMap, updated)
                ? updated
                : Charge(context, updated);
        }

        ValueShapeCache.ChargeMapInsert(context, typedMap, key, value, updated, typedMap.Values.Count + 1);
        return updated;
    }

    public static SandboxValue RemoveMapValue(SandboxValue key, SandboxValue map, SandboxContext context)
    {
        // Source map is already validated and immutable, so trust its entries (as reads and map.set do) and
        // validate only the key. Zero-shape scalar maps can subtract the removed entry exactly; complex maps
        // still fall back to a full shape walk because removal can change nested maxima.
        var typedMap = AsMapReadOnly(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count));
        var keyWasPresent = typedMap.Values.ContainsKey(key);
        var count = keyWasPresent ? typedMap.Values.Count - 1 : typedMap.Values.Count;
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(count, 32, minimumOne: true));
        var removed = typedMap.RemoveEntry(key);
        return ValueShapeCache.TryChargeScalarMapRemove(context, typedMap, removed, keyWasPresent)
            ? removed
            : Charge(context, removed);
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

    private static RecordValue AsRecordReadOnly(SandboxValue value)
        => value as RecordValue ?? throw Error(SandboxErrorCode.InvalidInput, "expected record value");

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
