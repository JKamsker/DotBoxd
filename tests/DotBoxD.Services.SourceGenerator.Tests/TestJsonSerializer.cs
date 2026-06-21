using System.Buffers;
using System.Text.Json;
using DotBoxD.Services.Serialization;

namespace DotBoxD.Services.SourceGenerator.Tests;

/// <summary>
/// Shared <see cref="ISerializer"/> test double backed by System.Text.Json. IncludeFields
/// lets it serialize ValueTuple (Item1/Item2/...), which the multi-argument wire format relies on.
/// </summary>
internal sealed class TestJsonSerializer : ISerializer
{
    private static readonly JsonSerializerOptions s_options = new() { IncludeFields = true };

    public void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        using var jw = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jw, value, s_options);
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> data) => JsonSerializer.Deserialize<T>(data.Span, s_options)!;

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => JsonSerializer.Deserialize(data.Span, type, s_options);
}
