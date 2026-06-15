namespace DotBoxD.Kernels;

public sealed partial class ResourceMeter
{
    private const int MaxUnchargedShapeScanValues = 62;

    private bool TryChargeFlatScalarValue(SandboxValue value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryMeasureFlatScalarValue(value, cancellationToken, out var shape))
        {
            return false;
        }

        ChargeMeasuredShape(shape);
        return true;
    }

    private static bool TryMeasureFlatScalarValue(
        SandboxValue value,
        CancellationToken cancellationToken,
        out ValueShape shape)
    {
        shape = new ValueShape(0, 0, 0, 0, 0, 0);
        if (value is ListValue list)
        {
            var values = list.Values;
            if (values.Count > MaxUnchargedShapeScanValues)
            {
                return false;
            }

            shape = shape with
            {
                Elements = values.Count,
                MaxListLength = values.Count,
                Depth = 1
            };
            for (var i = 0; i < values.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryAddScalarShape(values[i], ref shape))
                {
                    return false;
                }
            }

            return true;
        }

        return TryAddScalarShape(value, ref shape);
    }

    private static bool TryAddScalarShape(SandboxValue value, ref ValueShape shape)
    {
        switch (value)
        {
            case UnitValue or BoolValue or I32Value or I64Value or F64Value:
                return true;
            case StringValue text:
                AddTextShape(ref shape, SandboxLiteralConstraints.TextShape(text.Value));
                return true;
            case OpaqueIdValue id:
                AddTextShape(ref shape, SandboxLiteralConstraints.TextShape(id.Value));
                return true;
            case SandboxPathValue path:
                AddTextShape(ref shape, SandboxLiteralConstraints.TextShape(path.Value.RelativePath));
                return true;
            case SandboxUriValue uri:
                AddTextShape(ref shape, SandboxLiteralConstraints.TextShape(uri.Value.Value));
                return true;
            default:
                return false;
        }
    }

    private static void AddTextShape(ref ValueShape shape, ValueShape text)
    {
        shape = shape with
        {
            MaxStringLength = Math.Max(shape.MaxStringLength, text.MaxStringLength),
            StringBytes = AddChecked(shape.StringBytes, text.StringBytes, "string byte budget exhausted")
        };
    }
}
