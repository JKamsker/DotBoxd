namespace DotBoxd.Kernels.Runtime;

using DotBoxd.Kernels;

internal static class CompiledLiteralRuntime
{
    internal static SandboxValue ListLiteral(SandboxContext context, SandboxType itemType, SandboxValue[] values)
    {
        var list = ListLiteralValue(itemType, values);
        context.ChargeValue(list);
        return list;
    }

    internal static SandboxValue ListLiteralValue(SandboxType itemType, SandboxValue[] values)
    {
        var list = SandboxValue.FromList(values, itemType);
        SandboxValueValidator.RequireType(list, list.Type, "list literal item type mismatch");
        return list;
    }

    internal static SandboxValue MapLiteral(
        SandboxContext context,
        SandboxType keyType,
        SandboxType valueType,
        SandboxValue[] keys,
        SandboxValue[] values)
    {
        var map = MapLiteralValue(keyType, valueType, keys, values);
        context.ChargeValue(map);
        return map;
    }

    internal static SandboxValue MapLiteralValue(
        SandboxType keyType,
        SandboxType valueType,
        SandboxValue[] keys,
        SandboxValue[] values)
    {
        if (keys.Length != values.Length)
        {
            throw InvalidInput("map literal key/value count mismatch");
        }

        var entries = new Dictionary<SandboxValue, SandboxValue>(keys.Length);
        for (var i = 0; i < keys.Length; i++)
        {
            entries[keys[i]] = values[i];
        }

        var map = SandboxValue.FromMap(entries, keyType, valueType);
        SandboxValueValidator.RequireType(map, map.Type, "map literal entry type mismatch");
        return map;
    }

    internal static SandboxValue[] CreateValueArray(int count)
    {
        if (count < 0)
        {
            throw InvalidInput("array length must be non-negative");
        }

        return new SandboxValue[count];
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
