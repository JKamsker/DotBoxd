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
}
