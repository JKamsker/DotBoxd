using MessagePack;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 3 regression test for defect #6: <see cref="MessagePackRpcSerializer.CreateOptions"/>
/// rejects null resolver ELEMENTS (R1 #11) but silently accepts a null resolver ARRAY.
/// <para>
/// <c>CreateOptions((IFormatterResolver[]?)null)</c> hits <c>resolvers?.Length ?? 0 == 0</c>,
/// skips the population loop, and returns options with no custom formatters - no exception, no
/// diagnostic. A caller who accidentally passes null loses every custom resolver silently.
/// The correct behavior is to fail fast with <see cref="ArgumentNullException"/>, mirroring the
/// existing null-element guard. This test is RED against the current (unfixed) code because no
/// exception is thrown today.
/// </para>
/// </summary>
public sealed class Round3_CreateOptionsNullResolversTests
{
    [Fact]
    public void CreateOptions_NullResolverArray_ThrowsArgumentNullException()
    {
        // The cast disambiguates the params overload so null is the ARRAY itself, not a single
        // null element. On the unfixed code this returns options silently (RED).
        var ex = Assert.Throws<ArgumentNullException>(
            () => MessagePackRpcSerializer.CreateOptions((IFormatterResolver[]?)null!));

        Assert.Equal("resolvers", ex.ParamName);
    }
}
