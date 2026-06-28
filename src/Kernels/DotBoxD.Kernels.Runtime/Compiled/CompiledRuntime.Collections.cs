using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using SandboxContext = DotBoxD.Kernels.Sandbox.SandboxContext;

namespace DotBoxD.Kernels.Runtime;

// List and record collection entry points for the compiled runtime facade. Split out of CompiledRuntime.cs
// to keep that file under the line cap; these are part of the same partial type and share its private
// helpers (ChargeValue, AsListReadOnly, AsI32, I32, InvalidInput). These members are generated-code ABI:
// collection builders receive arrays that the compiler/runtime has just allocated and may transfer them
// through the internal owned-array construction path.
public static partial class CompiledRuntime
{
    public static SandboxValue ListOf(SandboxContext context, SandboxValue[] values)
    {
        context.ChargeFuel(SandboxCollectionFuel.Copy(values.Length));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(values.Length, 16));
        var itemType = values.Length == 0 ? SandboxType.Unit : values[0].Type;
        return ChargeValue(context, SandboxValue.FromOwnedList(values, itemType));
    }

    public static SandboxValue ListLiteral(SandboxContext context, SandboxType itemType, SandboxValue[] values)
        => CompiledLiteralRuntime.ListLiteral(context, itemType, values);

    public static SandboxValue ListLiteralValue(SandboxType itemType, SandboxValue[] values)
        => CompiledLiteralRuntime.ListLiteralValue(itemType, values);

    public static SandboxValue ListEmpty(SandboxContext context, SandboxType itemType)
    {
        context.ChargeFuel(SandboxCollectionFuel.Empty());
        context.ChargeAllocation(8);
        return ChargeValue(context, SandboxValue.FromList([], itemType));
    }

    public static SandboxValue ListCount(SandboxContext context, SandboxValue list)
    {
        var values = AsListReadOnly(list).Values;
        context.ChargeFuel(SandboxCollectionFuel.Read(values.Count));
        return I32(values.Count);
    }

    public static SandboxValue ListGet(SandboxContext context, SandboxValue list, SandboxValue index)
    {
        var values = AsListReadOnly(list).Values;
        context.ChargeFuel(SandboxCollectionFuel.Read(values.Count));
        var i = AsI32(index);
        if (i < 0 || i >= values.Count)
        {
            throw InvalidInput("list index is out of range");
        }

        return values[i];
    }

    public static SandboxValue ListAdd(SandboxContext context, SandboxValue list, SandboxValue item)
    {
        // Trust the already-validated, immutable source (as the read path does) and validate only the new
        // item; the deep-validating AsList re-walked the whole list on every add (O(n) per call).
        var source = AsListReadOnly(list);
        if (!SandboxValueTypeMatcher.MatchesExactType(item, source.ItemType))
        {
            throw InvalidInput("list item type mismatch");
        }

        var count = source.Values.Count;
        context.ChargeFuel(SandboxCollectionFuel.Copy(count, addedCount: 1));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            count,
            addedCount: 1,
            bytesPerElement: 16));

        // Structural-sharing append (O(log n)) plus incremental shape charging (O(1)); identical fuel/shape
        // to the old copy+walk path. Mirrors the interpreter's CollectionOperations.
        var appended = source.Append(item);
        ValueShapeCache.ChargeListAppend(context, source, item, appended, count + 1);
        return appended;
    }

    public static SandboxValue RecordNew(SandboxContext context, SandboxValue[] fields)
    {
        context.ChargeFuel(SandboxCollectionFuel.Copy(fields.Length));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(fields.Length, 16));
        return ChargeValue(context, SandboxValue.FromOwnedRecord(fields));
    }

    public static SandboxValue RecordGet(SandboxContext context, SandboxValue record, SandboxValue index)
    {
        var fields = AsRecordReadOnly(record).Fields;
        context.ChargeFuel(SandboxCollectionFuel.Read(fields.Count));
        var i = AsI32(index);
        if (i < 0 || i >= fields.Count)
        {
            throw InvalidInput("record field index is out of range");
        }

        return fields[i];
    }
}
