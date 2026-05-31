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

    public sealed class BinaryDto
    {
        public ReadOnlyMemory<byte> Data { get; set; }
    }
}
