using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Shared;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal static class SerializationCoverageTestSupport
{
    public static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        return serializer.Deserialize<T>(writer.WrittenMemory);
    }

    public static PlayerState SamplePlayerState() => new()
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
}

public sealed class AttributelessPoco
{
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
}

[MessagePackObject]
public sealed class BinaryFieldDto
{
    [Key(0)]
    public string Tag { get; set; } = string.Empty;

    [Key(1)]
    public ReadOnlyMemory<byte> Data { get; set; }
}
