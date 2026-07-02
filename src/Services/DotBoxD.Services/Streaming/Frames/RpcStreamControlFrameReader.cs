using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Streaming.Frames;

internal static class RpcStreamControlFrameReader
{
    public static bool TryRead(
        ReadOnlyMemory<byte> frame,
        MessageType expectedType,
        out int streamId)
    {
        streamId = 0;
        if (frame.Length != MessageFramer.HeaderSize ||
            !MessageFramer.TryReadFrameHeader(frame, out streamId, out var type) ||
            streamId <= 0 ||
            type != expectedType)
        {
            streamId = 0;
            return false;
        }

        return true;
    }
}
