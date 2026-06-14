using System.Buffers;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Serialization;
using ShaRPC.Serializers.MessagePack;
using Shared;
using Xunit;

namespace ShaRPC.Tests.Cov.Serialization;

/// <summary>
/// Behavioral coverage for <see cref="SerializerExtensions.SerializeToPayload{T}"/>. Asserts the
/// caller-owns-the-payload contract (disposable, re-usable bytes, deserializes back to the original
/// value) and that an exception from inside the serializer propagates without leaving a leaked
/// payload (the internal <c>using PooledBufferWriter</c> still releases its rented buffer).
/// </summary>
public sealed class SerializerExtensionsCoverageTests
{
    [Fact]
    public void SerializeToPayload_ReturnsOwnedPayload_ThatRoundTrips()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new MoveRequest { PlayerId = "p", X = 4f, Y = 5f, Z = 6f };

        using var payload = serializer.SerializeToPayload(value);

        Assert.NotNull(payload);
        Assert.True(payload.Length > 0);
        var result = serializer.Deserialize<MoveRequest>(payload.Memory);
        Assert.Equal("p", result.PlayerId);
        Assert.Equal(6f, result.Z);
    }

    [Fact]
    public void SerializeToPayload_PayloadMemoryMatchesDirectBufferWriterOutput()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new PlayerId { Id = "same" };

        using var payload = serializer.SerializeToPayload(value);

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);

        Assert.Equal(writer.WrittenMemory.ToArray(), payload.Memory.ToArray());
    }

    [Fact]
    public void SerializeToPayload_DisposeIsIdempotent()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = serializer.SerializeToPayload(new PlayerId { Id = "z" });

        payload.Dispose();
        // Second dispose must be a safe no-op (Payload.Dispose is idempotent).
        payload.Dispose();
    }

    [Fact]
    public void SerializeToPayload_EmptySerializerOutput_ReturnsUsablePayload()
    {
        var serializer = new MessagePackRpcSerializer();

        // null reference serializes to a single MessagePack nil byte — small but non-zero,
        // exercising DetachPayload on a real (non-Empty) rented buffer.
        using var payload = serializer.SerializeToPayload<PlayerId?>(null);

        Assert.True(payload.Length >= 1);
        var result = serializer.Deserialize<PlayerId?>(payload.Memory);
        Assert.Null(result);
    }

    [Fact]
    public void SerializeToPayload_SerializerThrows_PropagatesAndDoesNotLeak()
    {
        // A serializer whose Serialize always throws drives the failure path of SerializeToPayload:
        // the internal `using PooledBufferWriter` must dispose (return its rented buffer) before the
        // exception escapes. We assert the original exception type surfaces unchanged.
        var serializer = new ThrowingSerializer();

        var ex = Assert.Throws<InvalidOperationException>(
            () => serializer.SerializeToPayload(new PlayerId { Id = "boom" }));

        Assert.Equal("serialize-failure", ex.Message);
    }

    /// <summary>
    /// Minimal <see cref="ISerializer"/> whose <see cref="Serialize{T}"/> always throws, used to
    /// exercise the exceptional teardown path of the pooled-buffer extension helper.
    /// </summary>
    private sealed class ThrowingSerializer : ISerializer
    {
        public void Serialize<T>(IBufferWriter<byte> writer, T value) =>
            throw new InvalidOperationException("serialize-failure");

        public T Deserialize<T>(ReadOnlyMemory<byte> data) =>
            throw new NotSupportedException();

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            throw new NotSupportedException();
    }
}
