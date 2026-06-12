using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Streaming;

internal static class RpcStreamCompleteFrameReader
{
    public static bool TryRead(Payload frame, out int streamId) =>
        TryRead(frame.Memory, out streamId);

    public static bool TryRead(ReadOnlyMemory<byte> frame, out int streamId)
    {
        streamId = 0;
        if (frame.Length != MessageFramer.HeaderSize ||
            !MessageFramer.TryReadFrameHeader(frame, out streamId, out var type) ||
            streamId == 0 ||
            type != MessageType.StreamComplete)
        {
            return false;
        }

        return true;
    }
}
