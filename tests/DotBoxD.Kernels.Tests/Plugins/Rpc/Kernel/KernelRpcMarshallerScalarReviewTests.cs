using System.Reflection;

using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class KernelRpcMarshallerSurpriseTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DoubleToSingle_allows_non_finite_float_values(double value)
    {
        var result = InvokeDoubleToSingle(value);

        AssertFloat(value, result);
    }

    private static void AssertFloat(double expected, float actual)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(float.IsNaN(actual));
            return;
        }

        Assert.Equal((float)expected, actual);
    }

    private static float InvokeDoubleToSingle(double value)
    {
        var method = typeof(KernelRpcMarshaller).GetMethod(
            "DoubleToSingle",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsType<float>(method.Invoke(null, [value]));
    }
}
