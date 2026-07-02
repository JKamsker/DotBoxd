using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Model;

public sealed partial class ResourceMeter
{
    private const int MaxUnchargedShapeScanValues = 61;

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

    private void ChargeMeasuredShape(in ShapeInfo info)
    {
        var scanFuel = info.Nodes / 64;
        if (scanFuel > 0)
        {
            ChargeFuel(scanFuel);
            CheckDeadline();
        }

        ChargeMeasuredShape(info.Shape);
    }

    private void ChargeMeasuredShape(ValueShape shape)
    {
        if (shape.MaxListLength > Limits.MaxListLength)
        {
            throw Quota("list length budget exhausted");
        }

        if (shape.MaxMapEntries > Limits.MaxMapEntries)
        {
            throw Quota("map entry budget exhausted");
        }

        if (shape.Depth > Limits.MaxCollectionDepth)
        {
            throw Quota("collection depth budget exhausted");
        }

        CollectionElements = AddChecked(CollectionElements, shape.Elements, "collection element budget exhausted");
        if (CollectionElements > Limits.MaxTotalCollectionElements)
        {
            throw Quota("collection element budget exhausted");
        }

        ChargeStringShape(shape);
    }

    private void ChargeStringShape(ValueShape shape)
    {
        if (shape.MaxStringLength > Limits.MaxStringLength)
        {
            throw Quota("string length budget exhausted");
        }

        if (shape.StringBytes > 0)
        {
            ChargeAllocation(shape.StringBytes);
        }

        StringBytes = AddChecked(StringBytes, shape.StringBytes, "string byte budget exhausted");
        if (StringBytes > Limits.MaxTotalStringBytes)
        {
            throw Quota("string byte budget exhausted");
        }
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

        if (value is RecordValue record)
        {
            var fields = record.Fields;
            if (fields.Count > MaxUnchargedShapeScanValues)
            {
                return false;
            }

            shape = shape with
            {
                Elements = fields.Count,
                MaxListLength = fields.Count,
                Depth = 1
            };
            for (var i = 0; i < fields.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryAddScalarShape(fields[i], ref shape))
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
            case UnitValue or BoolValue or I32Value or I64Value or F64Value or GuidValue:
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
