namespace SafeIR;

internal static class SandboxValueShapeMeter
{
    public static ValueShape Measure(
        SandboxValue value,
        ResourceLimits? limits = null,
        CancellationToken cancellationToken = default,
        ResourceMeter? meter = null)
    {
        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Frame>();
        var shape = new ValueShape(0, 0, 0, 0, 0, 0);
        var scanned = 0;
        stack.Push(new Frame(value, Depth: 0, Exit: false));
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

            switch (frame.Value)
            {
                case StringValue text:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(text.Value), limits);
                    break;
                case OpaqueIdValue id:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(id.Value), limits);
                    break;
                case SandboxPathValue path:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(path.Value.RelativePath), limits);
                    break;
                case SandboxUriValue uri:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(uri.Value.Value), limits);
                    break;
                case ListValue list:
                    shape = AddList(shape, list, frame.Depth, active, stack, limits);
                    break;
                case MapValue map:
                    shape = AddMap(shape, map, frame.Depth, active, stack, limits);
                    break;
                case RecordValue record:
                    shape = AddRecord(shape, record, frame.Depth, active, stack, limits);
                    break;
                case UnitValue or BoolValue or I32Value or I64Value or F64Value:
                    break;
                default:
                    throw new SandboxRuntimeException(new SandboxError(
                        SandboxErrorCode.InvalidInput,
                        "unknown sandbox value kind is not supported"));
            }
        }

        return shape;
    }

    private static ValueShape AddList(
        ValueShape shape,
        ListValue list,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits)
    {
        Enter(list, active);
        var depth = parentDepth + 1;
        EnsureCollectionLimits(list.Values.Count, 0, depth, limits);
        stack.Push(new Frame(list, depth, Exit: true));
        for (var i = list.Values.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(list.Values[i], depth, Exit: false));
        }

        return AddCollection(shape, list.Values.Count, list.Values.Count, 0, depth, limits);
    }

    private static ValueShape AddMap(
        ValueShape shape,
        MapValue map,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits)
    {
        Enter(map, active);
        var depth = parentDepth + 1;
        EnsureCollectionLimits(0, map.Values.Count, depth, limits);
        stack.Push(new Frame(map, depth, Exit: true));
        foreach (var pair in map.Values)
        {
            stack.Push(new Frame(pair.Value, depth, Exit: false));
            stack.Push(new Frame(pair.Key, depth, Exit: false));
        }

        return AddCollection(shape, map.Values.Count, 0, map.Values.Count, depth, limits);
    }

    private static ValueShape AddRecord(
        ValueShape shape,
        RecordValue record,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits)
    {
        Enter(record, active);
        var depth = parentDepth + 1;
        // A record's fields are counted as collection elements and its nesting as one depth level, so a
        // record is budgeted exactly like a fixed-size list under the same element/depth limits.
        EnsureCollectionLimits(record.Fields.Count, 0, depth, limits);
        stack.Push(new Frame(record, depth, Exit: true));
        for (var i = record.Fields.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(record.Fields[i], depth, Exit: false));
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

    private static void Enter(object value, HashSet<object> active)
    {
        if (!active.Add(value))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "cyclic collection value is not supported"));
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

    private readonly record struct Frame(SandboxValue Value, int Depth, bool Exit);
}
