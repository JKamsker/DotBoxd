namespace SafeIR;

internal static class BindingReturnCost
{
    public static long MeasureBytes(SandboxValue value)
        => MeasureBytes(value, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static long MeasureBytes(SandboxValue value, HashSet<object> stack)
        => value switch {
            StringValue text => text.Value.Length * sizeof(char),
            SandboxPathValue path => path.Value.RelativePath.Length * sizeof(char),
            SandboxUriValue uri => uri.Value.Value.Length * sizeof(char),
            ListValue list => MeasureList(list, stack),
            MapValue map => MeasureMap(map, stack),
            _ => 0
        };

    private static long MeasureList(ListValue list, HashSet<object> stack)
    {
        Enter(list, stack);
        try {
            return list.Values.Sum(item => MeasureBytes(item, stack));
        }
        finally {
            stack.Remove(list);
        }
    }

    private static long MeasureMap(MapValue map, HashSet<object> stack)
    {
        Enter(map, stack);
        try {
            return map.Values.Sum(pair =>
                MeasureBytes(pair.Key, stack) + MeasureBytes(pair.Value, stack));
        }
        finally {
            stack.Remove(map);
        }
    }

    private static void Enter(object value, HashSet<object> stack)
    {
        if (!stack.Add(value)) {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "cyclic collection value is not supported"));
        }
    }
}
