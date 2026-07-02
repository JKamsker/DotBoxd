using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeNetworkBodyBufferTests
{
    [Fact]
    public void Initial_body_buffer_does_not_trust_declared_content_length()
    {
        var capacity = InitialBodyCapacity(contentLength: 512 * 1024, maxBytes: 1024 * 1024);

        Assert.Equal(256, capacity);
    }

    [Fact]
    public void Initial_body_buffer_still_uses_small_known_lengths()
    {
        var capacity = InitialBodyCapacity(contentLength: 12, maxBytes: 1024);

        Assert.Equal(12, capacity);
    }

    [Fact]
    public void Body_capacity_growth_clamps_before_array_length_overflow()
    {
        var currentLength = (Array.MaxLength / 2) + 1;
        var capacity = NextBodyCapacity(currentLength, Array.MaxLength);

        Assert.Equal(Array.MaxLength, capacity);
    }

    [Fact]
    public void Body_length_rejects_lengths_that_cannot_fit_in_an_array()
    {
        var ex = Assert.Throws<TargetInvocationException>(() =>
            _ = CheckedLength((long)Array.MaxLength + 1));

        var runtime = Assert.IsType<SandboxRuntimeException>(ex.InnerException);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, runtime.Error.Code);
    }

    private static int InitialBodyCapacity(long? contentLength, long maxBytes)
    {
        var type = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.Internal.SafeHttpBodyReader",
            throwOnError: true)!;
        var method = type.GetMethod(
            "InitialBodyCapacity",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (int)method.Invoke(null, [contentLength, maxBytes])!;
    }

    private static int NextBodyCapacity(int currentLength, int required)
    {
        var type = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.Internal.SafeHttpBodyReader",
            throwOnError: true)!;
        var method = type.GetMethod(
            "NextBodyCapacity",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (int)method.Invoke(null, [currentLength, required])!;
    }

    private static int CheckedLength(long length)
    {
        var type = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.Internal.SafeHttpBodyReader",
            throwOnError: true)!;
        var method = type.GetMethod(
            "CheckedLength",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (int)method.Invoke(null, [length])!;
    }
}
