using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    public static SandboxType SandboxTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SandboxTypeOf(type, 0, rejectNullableReferences: true);
    }

    internal static SandboxType HookResultSandboxTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SandboxTypeOf(type, 0, rejectNullableReferences: false);
    }

    // The list/map/record nesting depth is bounded so a self-referential DTO (e.g. a class with a property of
    // its own type) fails with a catchable NotSupportedException instead of an uncatchable StackOverflowException
    // when, say, ConventionEventAdapter is constructed for it. Kept at or below the kernel verifier's structural
    // depth limit (SandboxType.IsKnown defaults to maxDepth 8) so a produced type is never rejected at install.
    private const int MaxTypeNestingDepth = 8;

    private static SandboxType SandboxTypeOf(Type type, int depth)
        => SandboxTypeOf(type, depth, rejectNullableReferences: true);

    private static SandboxType SandboxTypeOf(Type type, int depth, bool rejectNullableReferences)
    {
        if (TryNullableSandboxType(type, depth, out var nullable))
        {
            return nullable;
        }

        if (type == typeof(bool))
            return SandboxType.Bool;
        if (type == typeof(int))
            return SandboxType.I32;
        if (type == typeof(long))
            return SandboxType.I64;
        if (type == typeof(double))
            return SandboxType.F64;
        // float widens losslessly to the sandbox's only floating kind (F64); decode narrows back exactly.
        if (type == typeof(float))
            return SandboxType.F64;
        if (type == typeof(string))
            return SandboxType.String;
        if (type == typeof(Guid))
            return SandboxType.Guid;
        if (type == typeof(DateOnly))
            return SandboxType.I32;
        if (type == typeof(TimeOnly))
            return SandboxType.I64;
        if (type == typeof(TimeSpan))
            return SandboxType.I64;
        if (type == typeof(CancellationToken))
            return SandboxType.Bool;
        if (IsDateTimeWireType(type))
        {
            RejectRecordTypeTooDeep(type, depth);
            return DateTimeWireSandboxType();
        }
        if (type == typeof(Index))
        {
            RejectRecordTypeTooDeep(type, depth);
            return IndexWireSandboxType();
        }
        if (type == typeof(Range))
        {
            RejectRangeTypeTooDeep(type, depth);
            return RangeWireSandboxType();
        }
        if (type.IsEnum)
            return EnumUsesI64(type) ? SandboxType.I64 : SandboxType.I32;

        ThrowIfUnsupportedFrameworkStruct(type);
        if (depth >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }

        if (ElementType(type) is { } elementType)
            return SandboxType.List(SandboxTypeOf(elementType, depth + 1, rejectNullableReferences));
        if (MapTypes(type) is { } mapTypes)
        {
            RejectUnsupportedMapKeyType(mapTypes.Key);
            var keyType = SandboxTypeOf(mapTypes.Key, depth + 1, rejectNullableReferences);
            // The kernel verifier only accepts a fixed set of scalar map keys (bool/int/long/string/opaque-id, not
            // Guid or double). Reject an unsupported key here with a catchable NotSupportedException instead of
            // producing a Map<Guid,V> that later fails IsKnown validation at install.
            if (!keyType.IsValidMapKey())
            {
                throw new NotSupportedException(
                    $"Kernel RPC service map key type '{mapTypes.Key}' is not a supported sandbox map key.");
            }

            return SandboxType.Map(keyType, SandboxTypeOf(mapTypes.Value, depth + 1, rejectNullableReferences));
        }

        if (DtoShape(type) is { } shape)
        {
            shape.RejectUnmatchedRequiredConstructor();
            if (rejectNullableReferences)
            {
                RejectNullableReferenceDtoShape(type, shape);
            }

            var fields = shape.Fields;
            var fieldTypes = new SandboxType[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                fieldTypes[i] = SandboxTypeOf(fields[i].Type, depth + 1, rejectNullableReferences);
            }

            return SandboxType.Record(fieldTypes);
        }

        throw new NotSupportedException($"Server extension has no sandbox type for '{type}'.");
    }

    private static void RejectUnsupportedMapKeyType(Type keyType)
    {
        if (keyType == typeof(CancellationToken))
        {
            throw new NotSupportedException(
                "Kernel RPC service map key type 'System.Threading.CancellationToken' is not supported; " +
                "CancellationToken marshals as a bool snapshot and would collapse distinct tokens.");
        }
    }

    private static void ThrowIfUnsupportedFrameworkStruct(Type type)
    {
        if (IsFrameworkStructWireType(type))
        {
            throw new NotSupportedException($"Server extension has no sandbox type for '{type}'.");
        }
    }

    private static void RejectRecordTypeTooDeep(Type type, int depth)
    {
        if (depth >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }
    }

    private static void RejectRangeTypeTooDeep(Type type, int depth)
    {
        if (depth + 1 >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }
    }
}
