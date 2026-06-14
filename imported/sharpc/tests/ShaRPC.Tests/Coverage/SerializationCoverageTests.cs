using System.Buffers;
using MessagePack;
using MessagePack.Resolvers;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Serialization;
using ShaRPC.Serializers.MessagePack;
using Shared;
using Xunit;

namespace ShaRPC.Tests.Cov.Serialization;

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
    private static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        return serializer.Deserialize<T>(writer.WrittenMemory);
    }

    // ---------------------------------------------------------------------
    // Construction paths
    // ---------------------------------------------------------------------

    [Fact]
    public void Options_DefaultConstructor_ExposesUntrustedSecurityOptions()
    {
        var serializer = new MessagePackRpcSerializer();

        var options = serializer.Options;

        Assert.NotNull(options);
        Assert.NotNull(options.Resolver);
        // CreateOptions() pins UntrustedData security; this is the observable contract that
        // protects the server from hostile depth/size payloads.
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

        // ContractlessStandardResolver lets a POCO with no MessagePack attributes serialize
        // by member name, which is the whole point of the Unity-compatible factory.
        var poco = new AttributelessPoco { Name = "neo", Score = 1337 };

        var result = RoundTrip(serializer, poco);

        Assert.NotNull(serializer.Options);
        Assert.Equal("neo", result.Name);
        Assert.Equal(1337, result.Score);
    }

    [Fact]
    public void CreateWithResolver_PrependsExtraResolver_AndRoundTripsAttributedModel()
    {
        // Passing a real extra resolver drives the extraCount > 0 branch of CreateOptions: the
        // copy loop runs and the resolver is placed ahead of the standard resolvers.
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
        // extraCount == 2: the for-loop copies both supplied resolvers before the two standard
        // resolvers are appended.
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

    // ---------------------------------------------------------------------
    // ReadOnlyMemory<byte> binary formatter (custom formatter, incl. nil branch)
    // ---------------------------------------------------------------------

    [Fact]
    public void RoundTrip_ReadOnlyMemoryBytes_RoundTripsBinaryPayload()
    {
        var serializer = new MessagePackRpcSerializer();
        ReadOnlyMemory<byte> data = new byte[] { 9, 8, 7, 6, 5 };

        var result = RoundTrip(serializer, data);

        Assert.Equal(new byte[] { 9, 8, 7, 6, 5 }, result.ToArray());
    }

    [Fact]
    public void Deserialize_NilIntoReadOnlyMemoryBytes_ReturnsEmpty()
    {
        // The custom formatter's Serialize never emits nil, so to drive the TryReadNil() == true
        // branch we hand it an explicit MessagePack nil byte (0xc0) and deserialize as
        // ReadOnlyMemory<byte>. Expected behavior: an empty (not null) buffer.
        var serializer = new MessagePackRpcSerializer();
        var nil = new byte[] { 0xc0 };

        var result = serializer.Deserialize<ReadOnlyMemory<byte>>(nil);

        Assert.True(result.IsEmpty);
        Assert.Equal(0, result.Length);
    }

    [Fact]
    public void RoundTrip_EmptyReadOnlyMemoryBytes_ReturnsEmpty()
    {
        var serializer = new MessagePackRpcSerializer();

        var result = RoundTrip(serializer, ReadOnlyMemory<byte>.Empty);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void RoundTrip_ModelWithBinaryField_PreservesBytes()
    {
        var serializer = new MessagePackRpcSerializer();
        var dto = new BinaryFieldDto { Tag = "blob", Data = new byte[] { 1, 2, 3, 4, 5 } };

        var result = RoundTrip(serializer, dto);

        Assert.Equal("blob", result.Tag);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, result.Data.ToArray());
    }

    // ---------------------------------------------------------------------
    // Malformed / truncated input failure paths
    // ---------------------------------------------------------------------

    [Fact]
    public void Deserialize_Truncated_ThrowsMessagePackSerializationException()
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, SamplePlayerState());

        // Lop off the back half so the reader runs out of bytes mid-structure.
        var full = writer.WrittenMemory.ToArray();
        var truncated = full.AsMemory(0, full.Length / 2);

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<PlayerState>(truncated));
    }

    [Fact]
    public void Deserialize_TypeMismatch_ThrowsMessagePackSerializationException()
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        // Serialize a bare string, then try to read it as an attributed array-shaped POCO.
        serializer.Serialize(writer, "not-an-object");

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<PlayerState>(writer.WrittenMemory));
    }

    [Fact]
    public void Deserialize_NonGenericTypeMismatch_ThrowsMessagePackSerializationException()
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, "scalar");

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize(writer.WrittenMemory, typeof(PlayerState)));
    }

    [Fact]
    public void Deserialize_GarbageBytes_ThrowsMessagePackSerializationException()
    {
        var serializer = new MessagePackRpcSerializer();
        // 0xc1 is the MessagePack "never used" type byte — always invalid.
        var garbage = new byte[] { 0xc1, 0xff, 0xff, 0xff };

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<PlayerState>(garbage));
    }

    [Fact]
    public void Deserialize_EmptyBuffer_Throws()
    {
        var serializer = new MessagePackRpcSerializer();

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<PlayerState>(ReadOnlyMemory<byte>.Empty));
    }

    // ---------------------------------------------------------------------
    // Helpers / fakes
    // ---------------------------------------------------------------------

    private static PlayerState SamplePlayerState() => new()
    {
        PlayerId = "p-42",
        Name = "Trinity",
        Level = 5,
        Health = 80,
        MaxHealth = 100,
        PositionX = 1.5f,
        PositionY = -2.25f,
        PositionZ = 99.125f,
    };

    /// <summary>POCO with no MessagePack attributes; only the contractless resolver can map it.</summary>
    public sealed class AttributelessPoco
    {
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    /// <summary>Attributed model carrying a <see cref="ReadOnlyMemory{T}"/> binary field.</summary>
    [MessagePackObject]
    public sealed class BinaryFieldDto
    {
        [Key(0)]
        public string Tag { get; set; } = string.Empty;

        [Key(1)]
        public ReadOnlyMemory<byte> Data { get; set; }
    }
}
