using DotBoxD.Codecs.MessagePack;
using MessagePack;
using MessagePack.Resolvers;
using Shared;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.SerializationCoverageTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class SerializationConstructionCoverageTests
{
    [Fact]
    public void Options_DefaultConstructor_ExposesUntrustedSecurityOptions()
    {
        var serializer = new MessagePackRpcSerializer();

        var options = serializer.Options;

        Assert.NotNull(options);
        Assert.NotNull(options.Resolver);
        Assert.Equal(MessagePackSecurity.UntrustedData, options.Security);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new MessagePackRpcSerializer(null!));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void Constructor_CustomOptions_UsesSuppliedOptionsInstance()
    {
        var options = MessagePackRpcSerializer.CreateOptions();

        var serializer = new MessagePackRpcSerializer(options);

        Assert.Same(options, serializer.Options);
    }

    [Fact]
    public void CreateUnityCompatible_RoundTripsAttributelessPoco()
    {
        var serializer = MessagePackRpcSerializer.CreateUnityCompatible();
        var poco = new AttributelessPoco { Name = "neo", Score = 1337 };

        var result = RoundTrip(serializer, poco);

        Assert.NotNull(serializer.Options);
        Assert.Equal("neo", result.Name);
        Assert.Equal(1337, result.Score);
    }

    [Fact]
    public void CreateWithResolver_PrependsExtraResolver_AndRoundTripsAttributedModel()
    {
        var serializer = MessagePackRpcSerializer.CreateWithResolver(StandardResolver.Instance);

        var state = SamplePlayerState();
        var result = RoundTrip(serializer, state);

        Assert.Equal(state.PlayerId, result.PlayerId);
        Assert.Equal(state.Level, result.Level);
        Assert.Equal(state.PositionZ, result.PositionZ);
    }

    [Fact]
    public void CreateOptions_WithMultipleResolvers_ProducesUsableSerializer()
    {
        var options = MessagePackRpcSerializer.CreateOptions(
            StandardResolver.Instance,
            ContractlessStandardResolver.Instance);

        var serializer = new MessagePackRpcSerializer(options);
        var request = new MoveRequest { PlayerId = "p1", X = 1f, Y = 2f, Z = 3f };

        var result = RoundTrip(serializer, request);

        Assert.Equal(MessagePackSecurity.UntrustedData, options.Security);
        Assert.Equal("p1", result.PlayerId);
        Assert.Equal(3f, result.Z);
    }

    [Fact]
    public void CreateOptions_WithNoResolvers_StillRoundTripsEnvelopeShapes()
    {
        var options = MessagePackRpcSerializer.CreateOptions();
        var serializer = new MessagePackRpcSerializer(options);

        var result = RoundTrip(serializer, new PlayerId { Id = "abc" });

        Assert.Equal("abc", result.Id);
    }
}
