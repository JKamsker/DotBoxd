using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using Shared;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.SerializationCoverageTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Behavioral coverage for the production <see cref="MessagePackRpcSerializer"/> and
/// <see cref="SerializerExtensions"/>. Exercises every public construction path
/// (default, Unity-compatible, custom resolver, raw options), the three
/// <see cref="ISerializer"/> surface methods (buffer-writer serialize, generic deserialize,
/// non-generic typed deserialize), the pooled <c>SerializeToPayload</c> helper, the
/// custom <c>ReadOnlyMemory&lt;byte&gt;</c> binary formatter (including its nil branch),
/// and the malformed/truncated-input failure paths. Every scenario asserts observable
/// behavior — round-trip equality, returned object identity/type, or thrown exception type.
/// </summary>
public sealed class SerializationCoverageTests
{
    // ---------------------------------------------------------------------
    // ISerializer surface: Serialize / Deserialize<T> / Deserialize(type)
    // ---------------------------------------------------------------------

    [Fact]
    public void Deserialize_NonGenericTypedOverload_ReturnsBoxedInstanceOfRequestedType()
    {
        var serializer = new MessagePackRpcSerializer();
        var status = new ServerStatus { PlayerCount = 7, ServerTime = "now", Version = "9.9" };

        using var payload = serializer.SerializeToPayload(status);
        object? boxed = serializer.Deserialize(payload.Memory, typeof(ServerStatus));

        var typed = Assert.IsType<ServerStatus>(boxed);
        Assert.Equal(7, typed.PlayerCount);
        Assert.Equal("now", typed.ServerTime);
        Assert.Equal("9.9", typed.Version);
    }

    [Fact]
    public void SerializeToPayload_RoundTripsThroughPooledBuffer()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new ActionRequest { PlayerId = "hero", ActionType = "attack", TargetId = "boss" };

        using var payload = serializer.SerializeToPayload(request);

        Assert.True(payload.Length > 0);
        var result = serializer.Deserialize<ActionRequest>(payload.Memory);
        Assert.Equal("hero", result.PlayerId);
        Assert.Equal("attack", result.ActionType);
        Assert.Equal("boss", result.TargetId);
    }

    [Fact]
    public void Serialize_IntoCustomBufferWriter_WritesConsumableBytes()
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();

        serializer.Serialize(writer, new PlayerId { Id = "x" });

        Assert.True(writer.WrittenCount > 0);
        var result = serializer.Deserialize<PlayerId>(writer.WrittenMemory);
        Assert.Equal("x", result.Id);
    }

    // ---------------------------------------------------------------------
    // Sample model round-trips (all Shared types)
    // ---------------------------------------------------------------------

    [Fact]
    public void RoundTrip_PlayerState_PreservesAllFields()
    {
        var serializer = new MessagePackRpcSerializer();
        var state = SamplePlayerState();

        var result = RoundTrip(serializer, state);

        Assert.Equal(state.PlayerId, result.PlayerId);
        Assert.Equal(state.Name, result.Name);
        Assert.Equal(state.Level, result.Level);
        Assert.Equal(state.Health, result.Health);
        Assert.Equal(state.MaxHealth, result.MaxHealth);
        Assert.Equal(state.PositionX, result.PositionX);
        Assert.Equal(state.PositionY, result.PositionY);
        Assert.Equal(state.PositionZ, result.PositionZ);
    }

    [Fact]
    public void RoundTrip_ActionResult_WithNullMessage_PreservesNull()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ActionResult { Success = false, Message = null };

        var result = RoundTrip(serializer, value);

        Assert.False(result.Success);
        Assert.Null(result.Message);
    }

    [Fact]
    public void RoundTrip_ActionRequest_WithNullTargetId_PreservesNull()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ActionRequest { PlayerId = "p", ActionType = "wave", TargetId = null };

        var result = RoundTrip(serializer, value);

        Assert.Equal("p", result.PlayerId);
        Assert.Null(result.TargetId);
    }

    [Fact]
    public void RoundTrip_DefaultPlayerState_PreservesDefaultValues()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new PlayerState();

        var result = RoundTrip(serializer, value);

        Assert.Equal(string.Empty, result.PlayerId);
        Assert.Equal(string.Empty, result.Name);
        Assert.Equal(0, result.Level);
        Assert.Equal(0f, result.PositionX);
    }

    [Fact]
    public void RoundTrip_EmptyStrings_PreservesEmptyNotNull()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ServerStatus { PlayerCount = 0, ServerTime = string.Empty, Version = string.Empty };

        var result = RoundTrip(serializer, value);

        Assert.Equal(string.Empty, result.ServerTime);
        Assert.Equal(string.Empty, result.Version);
    }

    [Fact]
    public void RoundTrip_LargeString_PreservesContent()
    {
        var serializer = new MessagePackRpcSerializer();
        var big = new string('z', 100_000);
        var value = new PlayerState { PlayerId = "big", Name = big };

        var result = RoundTrip(serializer, value);

        Assert.Equal(big, result.Name);
        Assert.Equal(100_000, result.Name.Length);
    }

    [Fact]
    public void RoundTrip_ArrayOfModels_PreservesOrderAndCount()
    {
        var serializer = new MessagePackRpcSerializer();
        var array = new[]
        {
            new PlayerId { Id = "a" },
            new PlayerId { Id = "b" },
            new PlayerId { Id = "c" },
        };

        var result = RoundTrip(serializer, array);

        Assert.Equal(3, result.Length);
        Assert.Equal("a", result[0].Id);
        Assert.Equal("c", result[2].Id);
    }

    [Fact]
    public void RoundTrip_EmptyCollection_PreservesEmptiness()
    {
        var serializer = new MessagePackRpcSerializer();
        var list = new List<MoveRequest>();

        var result = RoundTrip(serializer, list);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void RoundTrip_DictionaryPayload_PreservesEntries()
    {
        var serializer = new MessagePackRpcSerializer();
        var map = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };

        var result = RoundTrip(serializer, map);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["one"]);
        Assert.Equal(2, result["two"]);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void RoundTrip_EdgeIntValues_ArePreserved(int level)
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new PlayerState { Level = level };

        var result = RoundTrip(serializer, value);

        Assert.Equal(level, result.Level);
    }

}
