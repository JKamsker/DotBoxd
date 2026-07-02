using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;

namespace DotBoxD.Services.Streaming.Frames;

internal static class RpcStreamErrorFrameReader
{
    public static bool TryRead(
        Payload frame,
        ISerializer serializer,
        out int streamId,
        out RpcResponse response) =>
        TryRead(frame.Memory, serializer, out streamId, out response);

    public static bool TryRead(
        ReadOnlyMemory<byte> frame,
        ISerializer serializer,
        out int streamId,
        out RpcResponse response)
    {
        response = default;
        if (!MessageFramer.TryReadFrame(
                frame,
                out streamId,
                out var type,
                out var envelope,
                out var payload) ||
            !payload.IsEmpty ||
            streamId <= 0 ||
            type != MessageType.StreamError)
        {
            return false;
        }

        try
        {
            response = serializer.Deserialize<RpcResponse>(envelope);
        }
        catch
        {
            return false;
        }

        if (response.IsSuccess ||
            response.MessageId != streamId ||
            response.Stream is not null)
        {
            response = default;
            return false;
        }

        return true;
    }
}
