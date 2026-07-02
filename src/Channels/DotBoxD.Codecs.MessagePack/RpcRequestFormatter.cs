using System.Text;
using DotBoxD.Services.Protocol;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class RpcRequestFormatter : IMessagePackFormatter<RpcRequest>
{
    public static readonly RpcRequestFormatter Instance = new();

    private static readonly byte[] MessageIdKey = Encoding.UTF8.GetBytes("MessageId");
    private static readonly byte[] ServiceNameKey = Encoding.UTF8.GetBytes("ServiceName");
    private static readonly byte[] MethodNameKey = Encoding.UTF8.GetBytes("MethodName");
    private static readonly byte[] InstanceIdKey = Encoding.UTF8.GetBytes("InstanceId");
    private static readonly byte[] StreamsKey = Encoding.UTF8.GetBytes("Streams");

    private RpcRequestFormatter()
    {
    }

    public void Serialize(
        ref MessagePackWriter writer,
        RpcRequest value,
        MessagePackSerializerOptions options)
    {
        ThrowIfMissingRequiredName(value.ServiceName, nameof(RpcRequest.ServiceName));
        ThrowIfMissingRequiredName(value.MethodName, nameof(RpcRequest.MethodName));
        RpcRequestNameCache.Register(value.ServiceName);
        RpcRequestNameCache.Register(value.MethodName);

        writer.WriteMapHeader(5);
        writer.WriteString(MessageIdKey);
        writer.Write(value.MessageId);
        writer.WriteString(ServiceNameKey);
        WriteNullableString(ref writer, value.ServiceName);
        writer.WriteString(MethodNameKey);
        WriteNullableString(ref writer, value.MethodName);
        writer.WriteString(InstanceIdKey);
        WriteNullableString(ref writer, value.InstanceId);
        writer.WriteString(StreamsKey);
        GetStreamsFormatter(options).Serialize(ref writer, value.Streams!, options);
    }

    public RpcRequest Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadMapHeader();
        var request = new RpcRequest();
        var seenMessageId = false;
        var seenServiceName = false;
        var seenMethodName = false;
        var seenInstanceId = false;
        var seenStreams = false;

        for (var i = 0; i < count; i++)
        {
            switch (ReadField(ref reader))
            {
                case RpcRequestField.MessageId:
                    ThrowIfDuplicate(seenMessageId, nameof(RpcRequest.MessageId));
                    seenMessageId = true;
                    request.MessageId = reader.ReadInt32();
                    break;
                case RpcRequestField.ServiceName:
                    ThrowIfDuplicate(seenServiceName, nameof(RpcRequest.ServiceName));
                    seenServiceName = true;
                    request.ServiceName = ReadCachedName(ref reader)!;
                    break;
                case RpcRequestField.MethodName:
                    ThrowIfDuplicate(seenMethodName, nameof(RpcRequest.MethodName));
                    seenMethodName = true;
                    request.MethodName = ReadCachedName(ref reader)!;
                    break;
                case RpcRequestField.InstanceId:
                    ThrowIfDuplicate(seenInstanceId, nameof(RpcRequest.InstanceId));
                    seenInstanceId = true;
                    request.InstanceId = reader.ReadString();
                    break;
                case RpcRequestField.Streams:
                    ThrowIfDuplicate(seenStreams, nameof(RpcRequest.Streams));
                    seenStreams = true;
                    request.Streams = GetStreamsFormatter(options).Deserialize(ref reader, options);
                    break;
                default:
                    MessagePackEnvelopeSkipper.SkipUnknownField(ref reader, "RPC request");
                    break;
            }
        }

        if (!seenMessageId)
        {
            throw new MessagePackSerializationException(
                "RPC request is missing required MessageId.");
        }

        if (!seenServiceName || request.ServiceName is null)
        {
            throw new MessagePackSerializationException(
                "RPC request is missing required ServiceName.");
        }

        ThrowIfEmptyRequiredName(request.ServiceName, nameof(RpcRequest.ServiceName));

        if (!seenMethodName || request.MethodName is null)
        {
            throw new MessagePackSerializationException(
                "RPC request is missing required MethodName.");
        }

        ThrowIfEmptyRequiredName(request.MethodName, nameof(RpcRequest.MethodName));

        return request;
    }

    private static IMessagePackFormatter<RpcStreamHandle[]> GetStreamsFormatter(
        MessagePackSerializerOptions options)
    {
        return options.Resolver.GetFormatter<RpcStreamHandle[]>()
            ?? throw new MessagePackSerializationException(
            "No MessagePack formatter is registered for RPC stream handles.");
    }

    private static void ThrowIfDuplicate(bool alreadySeen, string fieldName)
    {
        if (alreadySeen)
        {
            throw new MessagePackSerializationException(
                $"RPC request contains duplicate {fieldName}.");
        }
    }

    private static void ThrowIfEmptyRequiredName(string value, string fieldName)
    {
        if (value.Length == 0)
        {
            throw new MessagePackSerializationException(
                $"RPC request contains empty required {fieldName}.");
        }
    }

    private static void ThrowIfMissingRequiredName(string? value, string fieldName)
    {
        if (value is null)
        {
            throw new MessagePackSerializationException(
                $"RPC request is missing required {fieldName}.");
        }

        ThrowIfEmptyRequiredName(value, fieldName);
    }

    private static void WriteNullableString(ref MessagePackWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        writer.Write(value);
    }

    private static string? ReadCachedName(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        return reader.TryReadStringSpan(out var utf8)
            ? RpcRequestNameCache.GetOrAdd(utf8)
            : RpcRequestNameCache.GetOrAdd(reader.ReadString()!);
    }

    private static RpcRequestField ReadField(ref MessagePackReader reader)
    {
        if (reader.TryReadStringSpan(out var utf8))
        {
            if (utf8.SequenceEqual(MessageIdKey))
            {
                return RpcRequestField.MessageId;
            }

            if (utf8.SequenceEqual(ServiceNameKey))
            {
                return RpcRequestField.ServiceName;
            }

            if (utf8.SequenceEqual(MethodNameKey))
            {
                return RpcRequestField.MethodName;
            }

            if (utf8.SequenceEqual(InstanceIdKey))
            {
                return RpcRequestField.InstanceId;
            }

            if (utf8.SequenceEqual(StreamsKey))
            {
                return RpcRequestField.Streams;
            }

            return RpcRequestField.Unknown;
        }

        return reader.ReadString() switch
        {
            "MessageId" => RpcRequestField.MessageId,
            "ServiceName" => RpcRequestField.ServiceName,
            "MethodName" => RpcRequestField.MethodName,
            "InstanceId" => RpcRequestField.InstanceId,
            "Streams" => RpcRequestField.Streams,
            _ => RpcRequestField.Unknown,
        };
    }

    private enum RpcRequestField
    {
        Unknown,
        MessageId,
        ServiceName,
        MethodName,
        InstanceId,
        Streams,
    }
}
