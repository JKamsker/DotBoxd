using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using SandboxContext = DotBoxD.Kernels.Sandbox.SandboxContext;

namespace DotBoxD.Kernels.Runtime.Internal;

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
        // Trust the already-validated, immutable source map (as the read path does); validate only the new
        // key/value. The deep-validating AsMap re-walked the whole map on every set (O(n) per call).
        var typedMap = AsMapReadOnly(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count, addedCount: 1));
        RequireType(value, typedMap.ValueType, "map value type mismatch");
        var isReplace = typedMap.Values.ContainsKey(key);
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            typedMap.Values.Count,
            isReplace ? 0 : 1,
            bytesPerElement: 32,
            minimumOne: true));
        var updated = typedMap.SetEntry(key, value);

        // New-key insert: charge incrementally from the source's cached shape (identical fuel/shape to a
        // full walk), turning repeated map.set from O(n^2) into O(n). Replacement changes a value subtree in
        // place and falls back to a full walk (rare in build loops). Mirrors the interpreter path.
        if (isReplace)
        {
            return ChargeValue(context, updated);
        }

        ValueShapeCache.ChargeMapInsert(context, typedMap, key, value, updated, typedMap.Values.Count + 1);
        return updated;
    }

    internal static SandboxValue Remove(SandboxContext context, SandboxValue map, SandboxValue key)
    {
        // Source map is already validated and immutable, so trust its entries (as reads and map.set do) and
        // validate only the key. The result still uses a full shape charge because removal changes an
        // existing subtree set and cannot be composed forward from the source shape without subtracting.
        var typedMap = AsMapReadOnly(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count));
        var count = typedMap.Values.ContainsKey(key) ? typedMap.Values.Count - 1 : typedMap.Values.Count;
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(count, 32, minimumOne: true));
        return ChargeValue(context, typedMap.RemoveEntry(key));
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
