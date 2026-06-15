namespace DotBoxD.Plugins;

using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// Converts between the compact server extension wire IR and the sandbox values consumed by installed verified
/// IR. The expected sandbox type is supplied by the server-side installed function signature.
/// </summary>
public static class KernelRpcValueConverter
{
    public static KernelRpcValue FromSandboxValue(SandboxValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            UnitValue => KernelRpcValue.Unit(),
            BoolValue boolean => KernelRpcValue.Bool(boolean.Value),
            I32Value number => KernelRpcValue.Int32(number.Value),
            I64Value number => KernelRpcValue.Int64(number.Value),
            F64Value number => KernelRpcValue.Double(number.Value),
            StringValue text => KernelRpcValue.String(text.Value),
            ListValue list => KernelRpcValue.List(ConvertList(list.Values)),
            RecordValue record => KernelRpcValue.Record(ConvertList(record.Fields)),
            _ => throw new NotSupportedException(
                $"Server extension IPC cannot marshal sandbox value '{value.GetType().Name}'.")
        };
    }

    public static SandboxValue ToSandboxValue(KernelRpcValue value, SandboxType expectedType)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        if (expectedType.Equals(SandboxType.Unit))
        {
            value.RequireKind(KernelRpcValueKind.Unit);
            return SandboxValue.Unit;
        }

        if (expectedType.Equals(SandboxType.Bool))
        {
            value.RequireKind(KernelRpcValueKind.Bool);
            return SandboxValue.FromBool(value.BoolValue);
        }

        if (expectedType.Equals(SandboxType.I32))
        {
            value.RequireKind(KernelRpcValueKind.I32);
            return SandboxValue.FromInt32(value.Int32Value);
        }

        if (expectedType.Equals(SandboxType.I64))
        {
            value.RequireKind(KernelRpcValueKind.I64);
            return SandboxValue.FromInt64(value.Int64Value);
        }

        if (expectedType.Equals(SandboxType.F64))
        {
            value.RequireKind(KernelRpcValueKind.F64);
            return SandboxValue.FromDouble(value.DoubleValue);
        }

        if (expectedType.Equals(SandboxType.String))
        {
            value.RequireKind(KernelRpcValueKind.String);
            return SandboxValue.FromString(value.TextValue);
        }

        if (expectedType.Name == "List" && expectedType.Arguments.Count == 1)
        {
            value.RequireKind(KernelRpcValueKind.List);
            var itemType = expectedType.Arguments[0];
            var items = new SandboxValue[value.Items.Length];
            for (var i = 0; i < value.Items.Length; i++)
            {
                items[i] = ToSandboxValue(value.Items[i], itemType);
            }

            return SandboxValue.FromList(items, itemType);
        }

        if (expectedType.IsRecord)
        {
            value.RequireKind(KernelRpcValueKind.Record);
            if (value.Items.Length != expectedType.Arguments.Count)
            {
                throw new NotSupportedException(
                    $"Server extension IPC record expected {expectedType.Arguments.Count} field(s) but received {value.Items.Length}.");
            }

            var fields = new SandboxValue[value.Items.Length];
            for (var i = 0; i < value.Items.Length; i++)
            {
                fields[i] = ToSandboxValue(value.Items[i], expectedType.Arguments[i]);
            }

            return SandboxValue.FromRecord(fields);
        }

        throw new NotSupportedException($"Server extension IPC cannot marshal expected sandbox type '{expectedType}'.");
    }

    private static KernelRpcValue[] ConvertList(IReadOnlyList<SandboxValue> values)
    {
        var converted = new KernelRpcValue[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            converted[i] = FromSandboxValue(values[i]);
        }

        return converted;
    }
}
