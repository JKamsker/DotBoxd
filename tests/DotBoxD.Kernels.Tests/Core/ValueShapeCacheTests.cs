using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class ValueShapeCacheTests
{
    [Fact]
    public void GetOrMeasure_returns_direct_scalar_shape_without_collection_cache()
    {
        var text = ValueShapeCache.GetOrMeasure(SandboxValue.FromString("fire"));
        var number = ValueShapeCache.GetOrMeasure(SandboxValue.FromInt32(42));

        Assert.Equal(1, text.Nodes);
        Assert.Equal(8, text.Shape.StringBytes);
        Assert.Equal(4, text.Shape.MaxStringLength);
        Assert.Equal(0, text.Shape.Elements);
        Assert.Equal(1, number.Nodes);
        Assert.Equal(0, number.Shape.StringBytes);
        Assert.Equal(0, number.Shape.Elements);
    }

    [Fact]
    public void GetOrMeasure_nested_collection_matches_full_walk()
    {
        var value = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("alpha")] = SandboxValue.FromList(
                    [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)], SandboxType.I32),
                [SandboxValue.FromString("b")] = SandboxValue.FromList(
                    [SandboxValue.FromInt32(3)], SandboxType.I32)
            },
            SandboxType.String,
            SandboxType.List(SandboxType.I32));

        var cached = ValueShapeCache.GetOrMeasure(value);
        var walked = SandboxValueShapeMeter.MeasureWithNodes(value);

        // The cached collection shape and frame count must be byte-identical to a full metering walk, or the
        // incremental list.add / map.set charge paths would diverge from a ChargeValue re-walk.
        Assert.Equal(walked.Nodes, cached.Nodes);
        Assert.Equal(walked.Shape, cached.Shape);
    }

    [Fact]
    public void GetOrMeasure_list_returns_same_shape_on_repeated_call()
    {
        var value = SandboxValue.FromList([SandboxValue.FromString("ab"), SandboxValue.FromInt32(7)]);

        var first = ValueShapeCache.GetOrMeasure(value);
        var second = ValueShapeCache.GetOrMeasure(value);

        Assert.Equal(first.Nodes, second.Nodes);
        Assert.Equal(first.Shape, second.Shape);
        Assert.Equal(SandboxValueShapeMeter.MeasureWithNodes(value).Shape, second.Shape);
    }

    [Fact]
    public void Set_is_returned_by_GetOrMeasure_on_collection_cache_hit()
    {
        // The incremental list.add / map.set charge path stores a precomputed shape via Set and relies on
        // GetOrMeasure returning exactly that on a cache hit instead of re-walking. Pin that contract.
        var donor = ValueShapeCache.GetOrMeasure(
            SandboxValue.FromList(
                [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)]));
        var target = SandboxValue.FromList([SandboxValue.FromInt32(9)]);

        ValueShapeCache.Set(target, donor);
        var got = ValueShapeCache.GetOrMeasure(target);

        Assert.Equal(donor.Nodes, got.Nodes);
        Assert.Equal(donor.Shape, got.Shape);
    }

    [Fact]
    public void TryChargeScalarMapRemove_present_key_matches_full_walk_charge()
    {
        var source = I32Map(entries: 128);
        var removed = source.RemoveEntry(SandboxValue.FromInt32(64));
        var optimizedContext = CreateContext();

        Assert.True(ValueShapeCache.TryChargeScalarMapRemove(
            optimizedContext,
            source,
            removed,
            keyWasPresent: true));

        AssertMatchesFullWalkCharge(optimizedContext, removed);
    }

    [Fact]
    public void TryChargeScalarMapRemove_missing_key_matches_full_walk_charge()
    {
        var source = I32Map(entries: 128);
        var removed = source.RemoveEntry(SandboxValue.FromInt32(500));
        var optimizedContext = CreateContext();

        Assert.True(ValueShapeCache.TryChargeScalarMapRemove(
            optimizedContext,
            source,
            removed,
            keyWasPresent: false));

        AssertMatchesFullWalkCharge(optimizedContext, removed);
    }

    [Fact]
    public void TryChargeScalarMapRemove_last_entry_matches_empty_map_full_walk_charge()
    {
        var source = I32Map(entries: 1);
        var removed = source.RemoveEntry(SandboxValue.FromInt32(0));
        var optimizedContext = CreateContext();

        Assert.True(ValueShapeCache.TryChargeScalarMapRemove(
            optimizedContext,
            source,
            removed,
            keyWasPresent: true));

        Assert.Empty(removed.Values);
        AssertMatchesFullWalkCharge(optimizedContext, removed);
    }

    [Fact]
    public void TryChargeScalarMapRemove_rejects_mismatched_removed_count()
    {
        var source = I32Map(entries: 1);

        Assert.False(ValueShapeCache.TryChargeScalarMapRemove(
            CreateContext(),
            source,
            source,
            keyWasPresent: true));
    }

    [Fact]
    public void TryChargeScalarMapRemove_rejects_present_key_on_empty_source()
    {
        var source = I32Map(entries: 0);

        Assert.False(ValueShapeCache.TryChargeScalarMapRemove(
            CreateContext(),
            source,
            source,
            keyWasPresent: true));
    }

    [Fact]
    public void TryChargeScalarMapReplace_matches_full_walk_charge()
    {
        var source = I32Map(entries: 128);
        var replaced = source.SetEntry(SandboxValue.FromInt32(64), SandboxValue.FromInt32(123));
        var optimizedContext = CreateContext();

        Assert.True(ValueShapeCache.TryChargeScalarMapReplace(
            optimizedContext,
            source,
            replaced));

        AssertMatchesFullWalkCharge(optimizedContext, replaced);
    }

    [Fact]
    public void TryChargeScalarMapRemove_rejects_values_with_string_shape()
    {
        var source = (MapValue)SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromInt32(1)] = SandboxValue.FromString("alpha"),
                [SandboxValue.FromInt32(2)] = SandboxValue.FromString("beta")
            },
            SandboxType.I32,
            SandboxType.String);
        var removed = source.RemoveEntry(SandboxValue.FromInt32(1));

        Assert.False(ValueShapeCache.TryChargeScalarMapRemove(
            CreateContext(),
            source,
            removed,
            keyWasPresent: true));
    }

    [Fact]
    public void TryChargeScalarMapRemove_missing_key_reuses_shape_with_string_shape()
    {
        var source = (MapValue)SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("a")] = SandboxValue.FromString("alpha"),
                [SandboxValue.FromString("b")] = SandboxValue.FromString("beta")
            },
            SandboxType.String,
            SandboxType.String);
        var removed = source.RemoveEntry(SandboxValue.FromString("missing"));
        var optimizedContext = CreateContext();

        Assert.True(ValueShapeCache.TryChargeScalarMapRemove(
            optimizedContext,
            source,
            removed,
            keyWasPresent: false));

        AssertMatchesFullWalkCharge(optimizedContext, removed);
    }

    [Fact]
    public void TryChargeScalarMapReplace_rejects_values_with_string_shape()
    {
        var source = (MapValue)SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromInt32(1)] = SandboxValue.FromString("alpha"),
                [SandboxValue.FromInt32(2)] = SandboxValue.FromString("beta")
            },
            SandboxType.I32,
            SandboxType.String);
        var replaced = source.SetEntry(SandboxValue.FromInt32(1), SandboxValue.FromString("gamma"));

        Assert.False(ValueShapeCache.TryChargeScalarMapReplace(
            CreateContext(),
            source,
            replaced));
    }

    private static MapValue I32Map(int entries)
    {
        var values = new Dictionary<SandboxValue, SandboxValue>();
        for (var i = 0; i < entries; i++)
        {
            values.Add(SandboxValue.FromInt32(i), SandboxValue.FromInt32(i * 10));
        }

        return (MapValue)SandboxValue.FromMap(values, SandboxType.I32, SandboxType.I32);
    }

    private static void AssertMatchesFullWalkCharge(SandboxContext optimizedContext, MapValue removed)
    {
        var optimized = optimizedContext.Budget.Snapshot();
        var walkedContext = CreateContext();
        walkedContext.ChargeValue(removed);
        var walked = walkedContext.Budget.Snapshot();
        var cached = ValueShapeCache.GetOrMeasure(removed);
        var measured = SandboxValueShapeMeter.MeasureWithNodes(removed);

        Assert.Equal(walked.FuelUsed, optimized.FuelUsed);
        Assert.Equal(walked.AllocatedBytes, optimized.AllocatedBytes);
        Assert.Equal(walked.CollectionElements, optimized.CollectionElements);
        Assert.Equal(walked.StringBytes, optimized.StringBytes);
        Assert.Equal(measured.Nodes, cached.Nodes);
        Assert.Equal(measured.Shape, cached.Shape);
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxWallTime: TimeSpan.FromMinutes(5),
            MaxAllocatedBytes: long.MaxValue,
            MaxMapEntries: int.MaxValue,
            MaxTotalCollectionElements: long.MaxValue);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}
