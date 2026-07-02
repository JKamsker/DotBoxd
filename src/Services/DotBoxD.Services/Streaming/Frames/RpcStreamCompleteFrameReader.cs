using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Streaming.Frames;

internal static class RpcStreamCompleteFrameReader
{
    public static bool TryRead(Payload frame, out int streamId) =>
        TryRead(frame.Memory, out streamId);

    public static bool TryRead(ReadOnlyMemory<byte> frame, out int streamId) =>
        RpcStreamControlFrameReader.TryRead(frame, MessageType.StreamComplete, out streamId);
}
