using System.Buffers;

namespace DotBoxd.Services.Serialization;

/// <summary>
/// Abstraction for message serialization.
/// </summary>
public interface ISerializer
{
    /// <summary>
    /// Serializes a value into the supplied buffer writer.
    /// </summary>
    void Serialize<T>(IBufferWriter<byte> writer, T value);

    /// <summary>
    /// Deserializes a value from a read-only memory region.
    /// </summary>
    T Deserialize<T>(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Deserializes a value from a read-only memory region to a specified type.
    /// </summary>
    object? Deserialize(ReadOnlyMemory<byte> data, Type type);
}
