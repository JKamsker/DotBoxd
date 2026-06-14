using System.Text;
using MessagePack;
using MessagePack.Formatters;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Serializers.MessagePack;

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
        var seenServiceName = false;
        var seenMethodName = false;

        for (var i = 0; i < count; i++)
        {
            switch (ReadField(ref reader))
            {
                case RpcRequestField.MessageId:
                    request.MessageId = reader.ReadInt32();
                    break;
                case RpcRequestField.ServiceName:
                    seenServiceName = true;
                    request.ServiceName = ReadCachedName(ref reader)!;
                    break;
                case RpcRequestField.MethodName:
                    seenMethodName = true;
                    request.MethodName = ReadCachedName(ref reader)!;
                    break;
                case RpcRequestField.InstanceId:
                    request.InstanceId = reader.ReadString();
                    break;
                case RpcRequestField.Streams:
                    request.Streams = GetStreamsFormatter(options).Deserialize(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (!seenServiceName || request.ServiceName is null)
        {
            throw new MessagePackSerializationException(
                "RPC request is missing required ServiceName.");
        }

        if (!seenMethodName || request.MethodName is null)
        {
            throw new MessagePackSerializationException(
                "RPC request is missing required MethodName.");
        }

        return request;
    }

    private static IMessagePackFormatter<RpcStreamHandle[]> GetStreamsFormatter(
        MessagePackSerializerOptions options)
    {
        return options.Resolver.GetFormatter<RpcStreamHandle[]>()
            ?? throw new MessagePackSerializationException(
                "No MessagePack formatter is registered for RPC stream handles.");
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
