namespace DotBoxd.Kernels.Runtime;

using DotBoxd.Kernels;

// Cohesive implementation detail for compiled-mode map operations.
//
// These are NOT part of the runtime facade allow-list: the public entry points
// stay on CompiledRuntime and simply delegate here (mirroring how Map literals
// already delegate to CompiledLiteralRuntime and bindings to
// CompiledBindingDispatcher). Behaviour and resource accounting are identical to
// the previous in-line CompiledRuntime implementations.
internal static class CompiledMapRuntime
{
    internal static SandboxValue Empty(SandboxContext context, SandboxType keyType, SandboxType valueType)
    {
        context.ChargeFuel(SandboxCollectionFuel.Empty());
        context.ChargeAllocation(16);
        return ChargeValue(context, SandboxValue.FromMap(new Dictionary<SandboxValue, SandboxValue>(), keyType, valueType));
    }

    internal static SandboxValue ContainsKey(SandboxContext context, SandboxValue map, SandboxValue key)
    {
        var typedMap = AsMapReadOnly(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Read(typedMap.Values.Count));
        return SandboxValue.FromBool(typedMap.Values.ContainsKey(key));
    }

    internal static SandboxValue Get(SandboxContext context, SandboxValue map, SandboxValue key)
    {
        var typedMap = AsMapReadOnly(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Read(typedMap.Values.Count));
        if (!typedMap.Values.TryGetValue(key, out var value))
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.NotFound, "map key was not found"));
        }

        return value;
    }

    internal static SandboxValue Set(SandboxContext context, SandboxValue map, SandboxValue key, SandboxValue value)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count, addedCount: 1));
        RequireType(value, typedMap.ValueType, "map value type mismatch");
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
        return ChargeValue(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    internal static SandboxValue Remove(SandboxContext context, SandboxValue map, SandboxValue key)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count));
        var count = typedMap.Values.ContainsKey(key) ? typedMap.Values.Count - 1 : typedMap.Values.Count;
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(count, 32, minimumOne: true));
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values);
        values.Remove(key);
        return ChargeValue(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    private static MapValue AsMap(SandboxValue value)
    {
        var map = value as MapValue ?? throw InvalidInput("expected map value");
        SandboxValueValidator.RequireType(map, map.Type, "map entry type mismatch");
        return map;
    }

    // Read-only map operations only need the runtime kind, not a recursive re-walk
    // of every element. Map contents are already validated against their declared
    // element types at trust boundaries (entrypoint inputs via EntrypointBinder and
    // binding returns via ChargeBindingReturn) and stay typed through every internal
    // constructor, so reads can trust the snapshotted value.
    private static MapValue AsMapReadOnly(SandboxValue value)
        => value as MapValue ?? throw InvalidInput("expected map value");

    private static SandboxValue RequireType(SandboxValue value, SandboxType expected, string message)
    {
        SandboxValueValidator.RequireType(value, expected, message);
        return value;
    }

    private static SandboxValue ChargeValue(SandboxContext context, SandboxValue value)
    {
        context.ChargeValue(value);
        return value;
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
