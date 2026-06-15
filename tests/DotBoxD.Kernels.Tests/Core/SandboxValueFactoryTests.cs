namespace DotBoxD.Kernels.Tests;

public sealed class SandboxValueFactoryTests
{
    [Fact]
    public void FromBool_reuses_immutable_bool_values()
    {
        var firstTrue = SandboxValue.FromBool(true);
        var secondTrue = SandboxValue.FromBool(true);
        var firstFalse = SandboxValue.FromBool(false);
        var secondFalse = SandboxValue.FromBool(false);

        Assert.Same(firstTrue, secondTrue);
        Assert.Same(firstFalse, secondFalse);
        Assert.NotSame(firstTrue, firstFalse);
        Assert.Equal(new BoolValue(true), firstTrue);
        Assert.Equal(new BoolValue(false), firstFalse);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(256)]
    public void FromInt32_reuses_common_immutable_i32_values(int value)
    {
        var first = SandboxValue.FromInt32(value);
        var second = SandboxValue.FromInt32(value);

        Assert.Same(first, second);
        Assert.Equal(new I32Value(value), first);
    }
}
