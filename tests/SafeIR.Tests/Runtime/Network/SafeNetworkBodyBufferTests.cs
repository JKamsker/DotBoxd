using System.Reflection;
using SafeIR.Runtime;

namespace SafeIR.Tests;

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

    private static int InitialBodyCapacity(long? contentLength, long maxBytes)
    {
        var method = typeof(SafeHttpClient).GetMethod(
            "InitialBodyCapacity",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (int)method.Invoke(null, [contentLength, maxBytes])!;
    }
}
