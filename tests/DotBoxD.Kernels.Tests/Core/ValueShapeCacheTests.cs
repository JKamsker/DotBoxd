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
}
