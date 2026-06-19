using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

public static class SandboxValueValidator
{
    public static void RequireType(SandboxValue value, SandboxType expectedType, string message)
        => RequireType(value, expectedType, SandboxErrorCode.InvalidInput, message);

    public static void RequireType(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message)
    {
        // Scalars have no nested structure, so they can never form a cycle and need
        // no traversal bookkeeping. Validate them inline to avoid allocating the
        // HashSet/Stack the recursive collection walk requires; this is the hot path
        // for every function return and binding argument check.
        if (value is not ListValue and not MapValue and not RecordValue)
        {
            RequireScalarType(value, expectedType, errorCode, message);
            return;
        }

        if (!expectedType.IsKnown())
        {
            throw Error(errorCode, message);
        }

        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Frame>();
        stack.Push(new Frame(value, expectedType, Exit: false));
        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            if (frame.Exit)
            {
                active.Remove(frame.Value);
                continue;
            }

            if (!SandboxValueTypeMatcher.MatchesValidationFrame(frame.Value, frame.ExpectedType))
            {
                throw Error(errorCode, message);
            }

            RequireScalarInvariants(frame.Value, errorCode, message);
            switch (frame.Value)
            {
                case OpaqueIdValue id:
                    RequireOpaqueId(id, errorCode, message);
                    break;
                case ListValue list:
                    PushList(list, frame.ExpectedType, active, stack, errorCode, message);
                    break;
                case MapValue map:
                    PushMap(map, frame.ExpectedType, active, stack, errorCode, message);
                    break;
                case RecordValue record:
                    PushRecord(record, frame.ExpectedType, active, stack, errorCode, message);
                    break;
            }
        }
    }

    private static void RequireScalarType(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message)
    {
        if (IsBuiltInScalarType(value, expectedType))
        {
            RequireScalarInvariants(value, errorCode, message);
            return;
        }

        if (!SandboxValueTypeMatcher.MatchesValidationFrame(value, expectedType) ||
            !expectedType.IsKnown())
        {
            throw Error(errorCode, message);
        }

        RequireScalarInvariants(value, errorCode, message);
        if (value is OpaqueIdValue id)
        {
            RequireOpaqueId(id, errorCode, message);
        }
    }

    private static void PushList(
        ListValue list,
        SandboxType expectedType,
        HashSet<object> active,
        Stack<Frame> stack,
        SandboxErrorCode errorCode,
        string message)
    {
        if (expectedType is not { Name: "List", Arguments.Count: 1 } ||
            list.ItemType != expectedType.Arguments[0])
        {
            throw Error(errorCode, message);
        }

        Enter(list, active, errorCode, message);
        stack.Push(new Frame(list, expectedType, Exit: true));
        for (var i = list.Values.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(list.Values[i], list.ItemType, Exit: false));
        }
    }

    private static void PushMap(
        MapValue map,
        SandboxType expectedType,
        HashSet<object> active,
        Stack<Frame> stack,
        SandboxErrorCode errorCode,
        string message)
    {
        if (expectedType is not { Name: "Map", Arguments.Count: 2 } ||
            map.KeyType != expectedType.Arguments[0] ||
            map.ValueType != expectedType.Arguments[1])
        {
            throw Error(errorCode, message);
        }

        Enter(map, active, errorCode, message);
        stack.Push(new Frame(map, expectedType, Exit: true));
        foreach (var pair in map.Values)
        {
            stack.Push(new Frame(pair.Value, map.ValueType, Exit: false));
            stack.Push(new Frame(pair.Key, map.KeyType, Exit: false));
        }
    }

    private static void PushRecord(
        RecordValue record,
        SandboxType expectedType,
        HashSet<object> active,
        Stack<Frame> stack,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!expectedType.IsRecord ||
            expectedType.Arguments.Count != record.Fields.Count)
        {
            throw Error(errorCode, message);
        }

        Enter(record, active, errorCode, message);
        stack.Push(new Frame(record, expectedType, Exit: true));
        for (var i = record.Fields.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(record.Fields[i], expectedType.Arguments[i], Exit: false));
        }
    }

    private static void Enter(
        object value,
        HashSet<object> active,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!active.Add(value))
        {
            throw Error(errorCode, message);
        }
    }

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message)
        => new(new SandboxError(code, message));

    private static bool IsBuiltInScalarType(SandboxValue value, SandboxType expectedType)
        => value switch
        {
            UnitValue => ReferenceEquals(expectedType, SandboxType.Unit),
            BoolValue => ReferenceEquals(expectedType, SandboxType.Bool),
            I32Value => ReferenceEquals(expectedType, SandboxType.I32),
            I64Value => ReferenceEquals(expectedType, SandboxType.I64),
            F64Value => ReferenceEquals(expectedType, SandboxType.F64),
            StringValue => ReferenceEquals(expectedType, SandboxType.String),
            GuidValue => ReferenceEquals(expectedType, SandboxType.Guid),
            SandboxPathValue => ReferenceEquals(expectedType, SandboxType.SandboxPath),
            SandboxUriValue => ReferenceEquals(expectedType, SandboxType.SandboxUri),
            _ => false
        };

    internal static void RequireScalarInvariants(
        SandboxValue value,
        SandboxErrorCode errorCode,
        string message)
    {
        switch (value)
        {
            case F64Value number when !double.IsFinite(number.Value):
                throw Error(errorCode, message);
            case SandboxPathValue path:
                if (path.Value?.RelativePath is not { } relativePath ||
                    !SandboxLiteralConstraints.IsPortableRelativePath(relativePath))
                {
                    throw Error(errorCode, message);
                }

                break;
            case SandboxUriValue uri:
                if (uri.Value?.Value is not { } valueText ||
                    !SandboxLiteralConstraints.IsSandboxUri(valueText))
                {
                    throw Error(errorCode, message);
                }

                break;
        }
    }

    private static void RequireOpaqueId(
        OpaqueIdValue id,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!SandboxType.IsKnownOpaqueId(id.TypeName) ||
            !SandboxLiteralConstraints.IsOpaqueId(id.Value))
        {
            throw Error(errorCode, message);
        }
    }

    private readonly record struct Frame(SandboxValue Value, SandboxType ExpectedType, bool Exit);
}
