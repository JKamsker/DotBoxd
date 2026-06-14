namespace SafeIR;

internal static class SandboxValidatedValueShapeMeter
{
    public static ValueShape Measure(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message,
        ResourceLimits? limits = null,
        CancellationToken cancellationToken = default,
        ResourceMeter? meter = null)
    {
        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Frame>();
        var shape = new ValueShape(0, 0, 0, 0, 0, 0);
        var scanned = 0;
        stack.Push(new Frame(value, expectedType, Depth: 0, Exit: false));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++scanned % 64 == 0)
            {
                meter?.ChargeFuel(1);
                meter?.CheckDeadline();
            }

            var frame = stack.Pop();
            if (frame.Exit)
            {
                active.Remove(frame.Value);
                continue;
            }

            ValidateKnownType(frame.Value, frame.ExpectedType, errorCode, message);
            SandboxValueValidator.RequireScalarInvariants(frame.Value, errorCode, message);
            switch (frame.Value)
            {
                case StringValue text:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(text.Value), limits);
                    break;
                case OpaqueIdValue id:
                    RequireOpaqueId(id, errorCode, message);
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(id.Value), limits);
                    break;
                case SandboxPathValue path:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(path.Value.RelativePath), limits);
                    break;
                case SandboxUriValue uri:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(uri.Value.Value), limits);
                    break;
                case ListValue list:
                    shape = AddList(shape, list, frame.ExpectedType, frame.Depth, active, stack, limits, errorCode, message);
                    break;
                case MapValue map:
                    shape = AddMap(shape, map, frame.ExpectedType, frame.Depth, active, stack, limits, errorCode, message);
                    break;
                case RecordValue record:
                    shape = AddRecord(shape, record, frame.ExpectedType, frame.Depth, active, stack, limits, errorCode, message);
                    break;
                case UnitValue or BoolValue or I32Value or I64Value or F64Value:
                    break;
            }
        }

        return shape;
    }

    private static ValueShape AddList(
        ValueShape shape,
        ListValue list,
        SandboxType expectedType,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
        SandboxErrorCode errorCode,
        string message)
    {
        if (expectedType is not { Name: "List", Arguments.Count: 1 } ||
            list.ItemType != expectedType.Arguments[0])
        {
            throw Error(errorCode, message);
        }

        Enter(list, active, errorCode, message);
        var depth = parentDepth + 1;
        EnsureCollectionLimits(list.Values.Count, 0, depth, limits);
        stack.Push(new Frame(list, expectedType, depth, Exit: true));
        for (var i = list.Values.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(list.Values[i], list.ItemType, depth, Exit: false));
        }

        return AddCollection(shape, list.Values.Count, list.Values.Count, 0, depth, limits);
    }

    private static ValueShape AddMap(
        ValueShape shape,
        MapValue map,
        SandboxType expectedType,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
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
        var depth = parentDepth + 1;
        EnsureCollectionLimits(0, map.Values.Count, depth, limits);
        stack.Push(new Frame(map, expectedType, depth, Exit: true));
        foreach (var pair in map.Values)
        {
            stack.Push(new Frame(pair.Value, map.ValueType, depth, Exit: false));
            stack.Push(new Frame(pair.Key, map.KeyType, depth, Exit: false));
        }

        return AddCollection(shape, map.Values.Count, 0, map.Values.Count, depth, limits);
    }

    private static ValueShape AddRecord(
        ValueShape shape,
        RecordValue record,
        SandboxType expectedType,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!expectedType.IsRecord || expectedType.Arguments.Count != record.Fields.Count)
        {
            throw Error(errorCode, message);
        }

        Enter(record, active, errorCode, message);
        var depth = parentDepth + 1;
        EnsureCollectionLimits(record.Fields.Count, 0, depth, limits);
        stack.Push(new Frame(record, expectedType, depth, Exit: true));
        for (var i = record.Fields.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(record.Fields[i], expectedType.Arguments[i], depth, Exit: false));
        }

        return AddCollection(shape, record.Fields.Count, record.Fields.Count, 0, depth, limits);
    }

    private static ValueShape AddCollection(
        ValueShape shape,
        int elements,
        int listLength,
        int mapEntries,
        int depth,
        ResourceLimits? limits)
    {
        var totalElements = AddLong(shape.Elements, elements, "collection element budget exhausted");
        if (limits is not null && totalElements > limits.MaxTotalCollectionElements)
        {
            throw Quota("collection element budget exhausted");
        }

        return shape with
        {
            Elements = totalElements,
            MaxListLength = Math.Max(shape.MaxListLength, listLength),
            MaxMapEntries = Math.Max(shape.MaxMapEntries, mapEntries),
            Depth = Math.Max(shape.Depth, depth)
        };
    }

    private static ValueShape AddText(ValueShape shape, ValueShape text, ResourceLimits? limits)
    {
        if (limits is not null && text.MaxStringLength > limits.MaxStringLength)
        {
            throw Quota("string length budget exhausted");
        }

        var stringBytes = AddLong(shape.StringBytes, text.StringBytes, "string byte budget exhausted");
        if (limits is not null && stringBytes > limits.MaxTotalStringBytes)
        {
            throw Quota("string byte budget exhausted");
        }

        return shape with
        {
            MaxStringLength = Math.Max(shape.MaxStringLength, text.MaxStringLength),
            StringBytes = stringBytes
        };
    }

    private static void EnsureCollectionLimits(int listLength, int mapEntries, int depth, ResourceLimits? limits)
    {
        if (limits is null)
        {
            return;
        }

        if (listLength > limits.MaxListLength)
        {
            throw Quota("list length budget exhausted");
        }

        if (mapEntries > limits.MaxMapEntries)
        {
            throw Quota("map entry budget exhausted");
        }

        if (depth > limits.MaxCollectionDepth)
        {
            throw Quota("collection depth budget exhausted");
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

    private static void ValidateKnownType(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!IsKnownValueKind(value) ||
            value.Type != expectedType ||
            !expectedType.IsKnown() ||
            expectedType.IsForbidden())
        {
            throw Error(errorCode, message);
        }
    }

    private static bool IsKnownValueKind(SandboxValue value)
        => value is UnitValue or BoolValue or I32Value or I64Value or F64Value or StringValue or OpaqueIdValue
            or SandboxPathValue or SandboxUriValue or ListValue or MapValue or RecordValue;

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

    private static long AddLong(long current, long amount, string quotaMessage)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw Quota(quotaMessage);
        }
    }

    private static SandboxRuntimeException Quota(string message)
        => new(new SandboxError(SandboxErrorCode.QuotaExceeded, message));

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message)
        => new(new SandboxError(code, message));

    private readonly record struct Frame(SandboxValue Value, SandboxType ExpectedType, int Depth, bool Exit);
}
