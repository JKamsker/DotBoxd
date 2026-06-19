using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// Memoizes the measured <see cref="ValueShape"/> (and metering-walk frame count) of immutable collection
/// values so that <c>list.add</c> / <c>map.set</c> can charge their result incrementally instead of
/// re-walking the entire collection on every operation.
///
/// Why this matters: <c>ChargeValue</c> walks the whole value to measure its shape (and charges
/// <c>nodes / 64</c> scan-fuel while doing so). Building a collection with repeated add/set therefore
/// re-walks 1, 2, 3, ... n elements — O(n^2) work and O(n^2) charged fuel. Because the result of an add/set
/// differs from its source by exactly one element/entry, the new shape and frame count can be composed in
/// O(1) from the source's cached values. The charged shape and fuel are byte-for-byte identical to a full
/// walk, so cross-mode differential accounting and golden fuel totals are preserved; only the redundant
/// re-walk is removed.
///
/// The cache is keyed by reference identity and holds no strong references, so cached shapes never keep a
/// value alive. Values are immutable, so a cached shape can never go stale.
/// </summary>
internal static class ValueShapeCache
{
    private static readonly ConditionalWeakTable<SandboxValue, StrongBox<ShapeInfo>> Cache = new();

    /// <summary>Returns the cached shape/frame-count for a value, measuring and caching collections on miss.</summary>
    public static ShapeInfo GetOrMeasure(SandboxValue value, CancellationToken cancellationToken = default)
    {
        // Scalars and text are O(1) to measure and not worth caching (one frame, no children).
        if (value is not (ListValue or MapValue or RecordValue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ShapeInfo(MeasureScalar(value), Nodes: 1);
        }

        if (Cache.TryGetValue(value, out var box))
        {
            return box.Value;
        }

        var measured = SandboxValueShapeMeter.MeasureWithNodes(value, cancellationToken);
        var info = new ShapeInfo(measured.Shape, measured.Nodes);
        Cache.AddOrUpdate(value, new StrongBox<ShapeInfo>(info));
        return info;
    }

    private static ValueShape MeasureScalar(SandboxValue value)
        => value switch
        {
            UnitValue or BoolValue or I32Value or I64Value or F64Value or GuidValue => new ValueShape(0, 0, 0, 0, 0, 0),
            StringValue text => SandboxLiteralConstraints.TextShape(text.Value),
            OpaqueIdValue id => SandboxLiteralConstraints.TextShape(id.Value),
            SandboxPathValue path => SandboxLiteralConstraints.TextShape(path.Value.RelativePath),
            SandboxUriValue uri => SandboxLiteralConstraints.TextShape(uri.Value.Value),
            _ => throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "unknown sandbox value kind is not supported"))
        };

    /// <summary>Records a precomputed shape for a value built by an incremental operation.</summary>
    public static void Set(SandboxValue value, ShapeInfo info)
        => Cache.AddOrUpdate(value, new StrongBox<ShapeInfo>(info));

    /// <summary>
    /// Charges the result of appending <paramref name="item"/> to <paramref name="source"/> (producing
    /// <paramref name="appended"/> of length <paramref name="newCount"/>) using incremental shape
    /// composition, then caches the result shape on <paramref name="appended"/>. Charged shape and fuel are
    /// identical to <c>context.ChargeValue(appended)</c>.
    /// </summary>
    public static void ChargeListAppend(
        Sandbox.SandboxContext context,
        ListValue source,
        SandboxValue item,
        SandboxValue appended,
        int newCount)
    {
        var cancellationToken = context.CancellationToken;
        var sourceInfo = GetOrMeasure(source, cancellationToken);
        var itemInfo = GetOrMeasure(item, cancellationToken);
        var combined = sourceInfo.Shape.Combine(itemInfo.Shape);
        var shape = combined with
        {
            Elements = combined.Elements + 1,
            MaxListLength = Math.Max(combined.MaxListLength, newCount)
        };
        var info = new ShapeInfo(shape, sourceInfo.Nodes + itemInfo.Nodes);
        context.ChargeComposedValue(info);
        Set(appended, info);
    }

    /// <summary>
    /// Charges the result of inserting a new <paramref name="key"/>/<paramref name="value"/> entry into
    /// <paramref name="source"/> (producing <paramref name="updated"/> with <paramref name="newCount"/>
    /// entries) using incremental shape composition. Only valid for inserts (key not already present);
    /// replacements fall back to a full walk by the caller. Charged shape and fuel are identical to
    /// <c>context.ChargeValue(updated)</c>.
    /// </summary>
    public static void ChargeMapInsert(
        Sandbox.SandboxContext context,
        MapValue source,
        SandboxValue key,
        SandboxValue value,
        SandboxValue updated,
        int newCount)
    {
        var cancellationToken = context.CancellationToken;
        var sourceInfo = GetOrMeasure(source, cancellationToken);
        var keyInfo = GetOrMeasure(key, cancellationToken);
        var valueInfo = GetOrMeasure(value, cancellationToken);
        var combined = sourceInfo.Shape.Combine(keyInfo.Shape).Combine(valueInfo.Shape);
        var shape = combined with
        {
            Elements = combined.Elements + 1,
            MaxMapEntries = Math.Max(combined.MaxMapEntries, newCount)
        };
        var info = new ShapeInfo(shape, sourceInfo.Nodes + keyInfo.Nodes + valueInfo.Nodes);
        context.ChargeComposedValue(info);
        Set(updated, info);
    }
}

/// <summary>A value's pure shape plus the number of frames its metering walk would process.</summary>
internal readonly record struct ShapeInfo(ValueShape Shape, long Nodes);
