using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public class MessagePackRpcSerializerTests
{
    [Fact]
    public void ReadOnlyMemoryByteFields_RoundTripAsBinaryPayload()
    {
        var serializer = new MessagePackRpcSerializer();
        var dto = new BinaryDto { Data = new byte[] { 1, 2, 3, 4 } };

        using var payload = serializer.SerializeToPayload(dto);
        var roundTrip = serializer.Deserialize<BinaryDto>(payload.Memory);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, roundTrip.Data.ToArray());
    }

    [Fact]
    public void RepeatedShortPayloadStrings_AreNotGloballyCached()
    {
        var serializer = new MessagePackRpcSerializer();

        var first = RoundTrip(serializer, "player-1");
        var second = RoundTrip(serializer, "player-1");

        Assert.Equal("player-1", first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void RpcRequestNames_RoundTripToCachedReference()
    {
        var serializer = new MessagePackRpcSerializer();

        var first = RoundTrip(serializer, CreateRequest(1, "GameService", "MovePlayerAsync"));
        var second = RoundTrip(serializer, CreateRequest(2, "GameService", "MovePlayerAsync"));

        Assert.Equal("GameService", first.ServiceName);
        Assert.Equal("MovePlayerAsync", first.MethodName);
        Assert.Same(first.ServiceName, second.ServiceName);
        Assert.Same(first.MethodName, second.MethodName);
    }

    [Fact]
    public void LongRpcRequestNames_AreNotCached()
    {
        var serializer = new MessagePackRpcSerializer();
        var serviceName = new string('S', 300);
        var methodName = new string('M', 300);

        var first = RoundTrip(serializer, CreateRequest(1, serviceName, methodName));
        var second = RoundTrip(serializer, CreateRequest(2, serviceName, methodName));

        Assert.Equal(serviceName, first.ServiceName);
        Assert.Equal(methodName, first.MethodName);
        Assert.NotSame(first.ServiceName, second.ServiceName);
        Assert.NotSame(first.MethodName, second.MethodName);
    }

    [Fact]
    public void LongStrings_AreNotCached()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new string('x', 300);

        var first = RoundTrip(serializer, value);
        var second = RoundTrip(serializer, value);

        Assert.Equal(value, first);
        Assert.Equal(value, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void CustomStringFormatter_TakesPrecedenceOverDefaultResolvers()
    {
        var resolver = new CustomStringResolver();
        var serializer = MessagePackRpcSerializer.CreateWithResolver(resolver);

        var result = RoundTrip(serializer, "player-1");

        Assert.Equal("custom:player-1", result);
        Assert.Equal(1, resolver.Formatter.DeserializeCalls);
    }

    private static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        using var payload = serializer.SerializeToPayload(value);
        return serializer.Deserialize<T>(payload.Memory);
    }

    private static RpcRequest CreateRequest(int messageId, string serviceName, string methodName) =>
        new()
        {
            MessageId = messageId,
            ServiceName = new string(serviceName.ToCharArray()),
            MethodName = new string(methodName.ToCharArray()),
        };

    public sealed class BinaryDto
    {
        public ReadOnlyMemory<byte> Data { get; set; }
    }

    private sealed class CustomStringResolver : IFormatterResolver
    {
        public CustomStringFormatter Formatter { get; } = new();

        public IMessagePackFormatter<T>? GetFormatter<T>() =>
            typeof(T) == typeof(string)
                ? (IMessagePackFormatter<T>)(object)Formatter
                : null;
    }

    internal sealed class CustomStringFormatter : IMessagePackFormatter<string?>
    {
        public int DeserializeCalls { get; private set; }

        public void Serialize(
            ref MessagePackWriter writer,
            string? value,
            MessagePackSerializerOptions options) =>
            writer.Write(value);

        public string? Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            DeserializeCalls++;
            return "custom:" + reader.ReadString();
        }
    }
}
