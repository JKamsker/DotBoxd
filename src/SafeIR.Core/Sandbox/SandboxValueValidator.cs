namespace SafeIR;

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

            if (!IsKnownValueKind(frame.Value) ||
                frame.Value.Type != frame.ExpectedType ||
                !frame.ExpectedType.IsKnown() ||
                frame.ExpectedType.IsForbidden())
            {
                throw Error(errorCode, message);
            }

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
            }
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
        foreach (var pair in map.Values.Reverse())
        {
            stack.Push(new Frame(pair.Value, map.ValueType, Exit: false));
            stack.Push(new Frame(pair.Key, map.KeyType, Exit: false));
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

    private static bool IsKnownValueKind(SandboxValue value)
        => value is UnitValue or BoolValue or I32Value or I64Value or F64Value or StringValue or OpaqueIdValue
            or SandboxPathValue or SandboxUriValue or ListValue or MapValue;

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
