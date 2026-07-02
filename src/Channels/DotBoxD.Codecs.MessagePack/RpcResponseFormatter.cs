using System.Text;
using DotBoxD.Services.Protocol;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class RpcResponseFormatter : IMessagePackFormatter<RpcResponse>
{
    public static readonly RpcResponseFormatter Instance = new();

    private static readonly byte[] MessageIdKey = Encoding.UTF8.GetBytes("MessageId");
    private static readonly byte[] IsSuccessKey = Encoding.UTF8.GetBytes("IsSuccess");
    private static readonly byte[] ErrorMessageKey = Encoding.UTF8.GetBytes("ErrorMessage");
    private static readonly byte[] ErrorTypeKey = Encoding.UTF8.GetBytes("ErrorType");
    private static readonly byte[] StreamKey = Encoding.UTF8.GetBytes("Stream");

    private RpcResponseFormatter()
    {
    }

    public void Serialize(
        ref MessagePackWriter writer,
        RpcResponse value,
        MessagePackSerializerOptions options)
    {
        ValidateEnvelope(value);
        writer.WriteMapHeader(5);
        writer.WriteString(MessageIdKey);
        writer.Write(value.MessageId);
        writer.WriteString(IsSuccessKey);
        writer.Write(value.IsSuccess);
        writer.WriteString(ErrorMessageKey);
        WriteNullableString(ref writer, value.ErrorMessage);
        writer.WriteString(ErrorTypeKey);
        WriteNullableString(ref writer, value.ErrorType);
        writer.WriteString(StreamKey);
        WriteNullableStream(ref writer, value.Stream, options);
    }

    public RpcResponse Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadMapHeader();
        var response = new RpcResponse();
        var seenMessageId = false;
        var seenIsSuccess = false;
        var seenErrorMessage = false;
        var seenErrorType = false;
        var seenStream = false;

        for (var i = 0; i < count; i++)
        {
            switch (ReadField(ref reader))
            {
                case RpcResponseField.MessageId:
                    ThrowIfDuplicate(seenMessageId, nameof(RpcResponse.MessageId));
                    seenMessageId = true;
                    response.MessageId = reader.ReadInt32();
                    break;
                case RpcResponseField.IsSuccess:
                    ThrowIfDuplicate(seenIsSuccess, nameof(RpcResponse.IsSuccess));
                    seenIsSuccess = true;
                    response.IsSuccess = reader.ReadBoolean();
                    break;
                case RpcResponseField.ErrorMessage:
                    ThrowIfDuplicate(seenErrorMessage, nameof(RpcResponse.ErrorMessage));
                    seenErrorMessage = true;
                    response.ErrorMessage = reader.ReadString();
                    break;
                case RpcResponseField.ErrorType:
                    ThrowIfDuplicate(seenErrorType, nameof(RpcResponse.ErrorType));
                    seenErrorType = true;
                    response.ErrorType = reader.ReadString();
                    break;
                case RpcResponseField.Stream:
                    ThrowIfDuplicate(seenStream, nameof(RpcResponse.Stream));
                    seenStream = true;
                    response.Stream = ReadNullableStream(ref reader, options);
                    break;
                default:
                    MessagePackEnvelopeSkipper.SkipUnknownField(ref reader, "RPC response");
                    break;
            }
        }

        if (!seenMessageId)
        {
            throw new MessagePackSerializationException(
                "RPC response is missing required MessageId.");
        }

        if (!seenIsSuccess)
        {
            throw new MessagePackSerializationException(
                "RPC response is missing required IsSuccess.");
        }

        ValidateEnvelope(response);
        return response;
    }

    private static void ValidateEnvelope(RpcResponse response)
    {
        if (response.IsSuccess &&
            (response.ErrorMessage is not null || response.ErrorType is not null))
        {
            throw new MessagePackSerializationException(
                "Successful RPC response must not contain error fields.");
        }

        if (!response.IsSuccess && response.Stream is not null)
        {
            throw new MessagePackSerializationException(
                "Error RPC response must not contain a stream handle.");
        }
    }

    private static IMessagePackFormatter<RpcStreamHandle> GetStreamFormatter(
        MessagePackSerializerOptions options)
    {
        return options.Resolver.GetFormatter<RpcStreamHandle>()
            ?? throw new MessagePackSerializationException(
                "No MessagePack formatter is registered for RPC stream handles.");
    }

    private static void ThrowIfDuplicate(bool alreadySeen, string fieldName)
    {
        if (alreadySeen)
        {
            throw new MessagePackSerializationException(
                $"RPC response contains duplicate {fieldName}.");
        }
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

    private static void WriteNullableStream(
        ref MessagePackWriter writer,
        RpcStreamHandle? value,
        MessagePackSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNil();
            return;
        }

        GetStreamFormatter(options).Serialize(ref writer, value.GetValueOrDefault(), options);
    }

    private static RpcStreamHandle? ReadNullableStream(
        ref MessagePackReader reader,
        MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        return GetStreamFormatter(options).Deserialize(ref reader, options);
    }

    private static RpcResponseField ReadField(ref MessagePackReader reader)
    {
        if (reader.TryReadStringSpan(out var utf8))
        {
            if (utf8.SequenceEqual(MessageIdKey))
            {
                return RpcResponseField.MessageId;
            }

            if (utf8.SequenceEqual(IsSuccessKey))
            {
                return RpcResponseField.IsSuccess;
            }

            if (utf8.SequenceEqual(ErrorMessageKey))
            {
                return RpcResponseField.ErrorMessage;
            }

            if (utf8.SequenceEqual(ErrorTypeKey))
            {
                return RpcResponseField.ErrorType;
            }

            if (utf8.SequenceEqual(StreamKey))
            {
                return RpcResponseField.Stream;
            }

            return RpcResponseField.Unknown;
        }

        return reader.ReadString() switch
        {
            "MessageId" => RpcResponseField.MessageId,
            "IsSuccess" => RpcResponseField.IsSuccess,
            "ErrorMessage" => RpcResponseField.ErrorMessage,
            "ErrorType" => RpcResponseField.ErrorType,
            "Stream" => RpcResponseField.Stream,
            _ => RpcResponseField.Unknown,
        };
    }

    private enum RpcResponseField
    {
        Unknown,
        MessageId,
        IsSuccess,
        ErrorMessage,
        ErrorType,
        Stream,
    }
}
