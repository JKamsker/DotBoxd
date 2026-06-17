using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox.Values;

internal static partial class SandboxValidatedValueShapeMeter
{
    private static ValueShape AddList(
        ValueShape shape,
        ListValue list,
        SandboxType expectedType,
        int parentDepth,
        HashSet<object> active,
        Stack<Frame> stack,
        ResourceLimits? limits,
        ValidationFailure failure)
    {
        if (expectedType is not { Name: "List", Arguments.Count: 1 } ||
            list.ItemType != expectedType.Arguments[0])
        {
            throw Error(failure);
        }

        Enter(list, active, failure);
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
        ValidationFailure failure)
    {
        if (expectedType is not { Name: "Map", Arguments.Count: 2 } ||
            map.KeyType != expectedType.Arguments[0] ||
            map.ValueType != expectedType.Arguments[1])
        {
            throw Error(failure);
        }

        Enter(map, active, failure);
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
        ValidationFailure failure)
    {
        if (!expectedType.IsRecord || expectedType.Arguments.Count != record.Fields.Count)
        {
            throw Error(failure);
        }

        Enter(record, active, failure);
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
}
