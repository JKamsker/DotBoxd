using System.Buffers;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Serializers.MessagePack;

/// <summary>
/// MessagePack-based serializer implementation.
/// </summary>
public sealed class MessagePackRpcSerializer : ISerializer
{
    private readonly MessagePackSerializerOptions _options;

    /// <summary>
    /// Gets the MessagePack options used for RPC envelopes and method payloads.
    /// </summary>
    public MessagePackSerializerOptions Options => _options;

    /// <summary>
    /// Creates a new MessagePack serializer with default options.
    /// </summary>
    public MessagePackRpcSerializer() : this(CreateDefaultOptions())
    {
    }

    /// <summary>
    /// Creates a new MessagePack serializer with custom options.
    /// </summary>
    public MessagePackRpcSerializer(MessagePackSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Creates a MessagePack serializer optimized for Unity compatibility.
    /// Uses ContractlessStandardResolver which doesn't require attributes.
    /// </summary>
    public static MessagePackRpcSerializer CreateUnityCompatible()
    {
        var options = CreateOptions(ContractlessStandardResolver.Instance);

        return new MessagePackRpcSerializer(options);
    }

    /// <summary>
    /// Creates a serializer using the supplied resolver plus ShaRPC's binary payload formatters.
    /// </summary>
    public static MessagePackRpcSerializer CreateWithResolver(IFormatterResolver resolver) =>
        new(CreateOptions(resolver));

    /// <summary>
    /// Creates MessagePack options that include ShaRPC's payload formatters before user resolvers.
    /// </summary>
    public static MessagePackSerializerOptions CreateOptions(params IFormatterResolver[] resolvers)
    {
        var extraCount = resolvers?.Length ?? 0;
        var effectiveResolvers = new IFormatterResolver[extraCount + 2];
        for (var i = 0; i < extraCount; i++)
        {
            effectiveResolvers[i] = resolvers![i];
        }

        effectiveResolvers[extraCount] = StandardResolver.Instance;
        effectiveResolvers[extraCount + 1] = ContractlessStandardResolver.Instance;

        return MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[] { ReadOnlyMemoryByteFormatter.Instance },
                effectiveResolvers))
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }

    private static MessagePackSerializerOptions CreateDefaultOptions()
    {
        return CreateOptions();
    }

    public void Serialize<T>(System.Buffers.IBufferWriter<byte> writer, T value)
    {
        MessagePackSerializer.Serialize(writer, value, _options);
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        return MessagePackSerializer.Deserialize<T>(data, _options);
    }

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        return MessagePackSerializer.Deserialize(type, data, _options);
    }

    private sealed class ReadOnlyMemoryByteFormatter : IMessagePackFormatter<ReadOnlyMemory<byte>>
    {
        public static readonly ReadOnlyMemoryByteFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer,
            ReadOnlyMemory<byte> value,
            MessagePackSerializerOptions options) =>
            writer.Write(value.Span);

        public ReadOnlyMemory<byte> Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            var bytes = reader.ReadBytes();
            return bytes is { } sequence
                ? sequence.ToArray()
                : ReadOnlyMemory<byte>.Empty;
        }
    }
}
