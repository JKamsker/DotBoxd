using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    internal static void RejectNullableValueTypesForServerExtension(Type type)
    {
        if (ContainsNullableValueType(type, []))
        {
            throw new NotSupportedException($"Server extension nullable type '{type}' is not supported.");
        }
    }

    private static bool TryNullableToSandboxValue(object? value, Type type, out SandboxValue sandbox)
    {
        if (Nullable.GetUnderlyingType(type) is not { } underlying)
        {
            sandbox = null!;
            return false;
        }

        EnsureSupportedNullableUnderlying(underlying);
        sandbox = SandboxValue.FromOwnedRecord(
        [
            SandboxValue.FromBool(value is not null),
            value is null ? NullableZeroValue(underlying) : ToSandboxValue(value, underlying)
        ]);
        return true;
    }

    private static bool TryNullableFromSandboxValue(SandboxValue value, Type type, out object? result)
    {
        if (Nullable.GetUnderlyingType(type) is not { } underlying)
        {
            result = null;
            return false;
        }

        EnsureSupportedNullableUnderlying(underlying);
        if (value is not RecordValue { Fields.Count: 2 } record ||
            record.Fields[0] is not BoolValue hasValue)
        {
            throw new NotSupportedException(
                $"Server extension cannot marshal a sandbox value to nullable type '{type}'.");
        }

        result = hasValue.Value ? FromSandboxValue(record.Fields[1], underlying) : null;
        return true;
    }

    private static bool TryNullableFromKernelRpcValue(KernelRpcValue value, Type type, out object? result)
    {
        if (Nullable.GetUnderlyingType(type) is not { } underlying)
        {
            result = null;
            return false;
        }

        EnsureSupportedNullableUnderlying(underlying);
        value.RequireKind(KernelRpcValueKind.Record);
        if (value.ItemCount != 2)
        {
            throw new NotSupportedException(
                $"Server extension cannot marshal a kernel RPC value to nullable type '{type}'.");
        }

        result = value.GetItem(0).BoolValue ? FromKernelRpcValue(value.GetItem(1), underlying) : null;
        return true;
    }

    private static bool TryNullableSandboxType(Type type, int depth, out SandboxType sandboxType)
    {
        if (Nullable.GetUnderlyingType(type) is not { } underlying)
        {
            sandboxType = null!;
            return false;
        }

        if (depth >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }

        EnsureSupportedNullableUnderlying(underlying);
        sandboxType = SandboxType.Record([SandboxType.Bool, SandboxTypeOf(underlying, depth + 1)]);
        return true;
    }

    private static SandboxValue NullableZeroValue(Type underlying)
    {
        if (underlying == typeof(bool))
            return SandboxValue.FromBool(false);
        if (underlying == typeof(int))
            return SandboxValue.FromInt32(0);
        if (underlying == typeof(long))
            return SandboxValue.FromInt64(0L);
        if (underlying == typeof(float) || underlying == typeof(double))
            return SandboxValue.FromDouble(0D);
        if (underlying == typeof(Guid))
            return SandboxValue.FromGuid(Guid.Empty);
        if (underlying.IsEnum)
            return EnumUsesI64(underlying) ? SandboxValue.FromInt64(0L) : SandboxValue.FromInt32(0);

        throw UnsupportedNullableUnderlying(underlying);
    }

    private static void EnsureSupportedNullableUnderlying(Type underlying)
    {
        if (underlying == typeof(bool) ||
            underlying == typeof(int) ||
            underlying == typeof(long) ||
            underlying == typeof(float) ||
            underlying == typeof(double) ||
            underlying == typeof(Guid) ||
            underlying.IsEnum)
        {
            return;
        }

        throw UnsupportedNullableUnderlying(underlying);
    }

    private static NotSupportedException UnsupportedNullableUnderlying(Type underlying)
        => new($"Kernel RPC service nullable type '{typeof(Nullable<>).MakeGenericType(underlying)}' is not supported.");

    private static bool ContainsNullableValueType(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (Nullable.GetUnderlyingType(type) is not null)
        {
            return true;
        }

        if (ElementType(type) is { } elementType && ContainsNullableValueType(elementType, visited))
        {
            return true;
        }

        if (MapTypes(type) is { } mapTypes &&
            (ContainsNullableValueType(mapTypes.Key, visited) ||
             ContainsNullableValueType(mapTypes.Value, visited)))
        {
            return true;
        }

        if (DtoShape(type) is { } shape)
        {
            foreach (var field in shape.Fields)
            {
                if (ContainsNullableValueType(field.Type, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
