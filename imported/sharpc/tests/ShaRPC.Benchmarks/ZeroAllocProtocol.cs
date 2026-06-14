using System.Buffers.Binary;

namespace ShaRPC.Benchmarks;

internal static class ZeroAllocProtocol
{
    public const int HeaderSize = 8;
    public const int RegisterRoute = 1;
    public const int MoveRoute = 2;
    public const int StatusRoute = 3;
    public const int GetStateRoute = 4;
    public const int ActionRoute = 5;
    public const int HeartbeatRoute = 6;

    public static ReadOnlySpan<byte> ReadPayload(ReadOnlySpan<byte> frame, int expectedRoute)
    {
        var length = BinaryPrimitives.ReadInt32LittleEndian(frame);
        var route = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(4));
        if (length != frame.Length || route != expectedRoute)
        {
            throw new InvalidOperationException("Invalid benchmark frame.");
        }

        return frame.Slice(HeaderSize);
    }

    public static int WriteAckResponse(Span<byte> frame, int route)
    {
        WriteFrame(frame, route, payloadLength: 0);
        return HeaderSize;
    }

    public static int WritePlayerStateResponse(Span<byte> frame, int route, PlayerStateValue player)
    {
        WriteFrame(frame, route, PlayerStateValue.Size);
        PlayerStateValue.Write(frame.Slice(HeaderSize), player);
        return HeaderSize + PlayerStateValue.Size;
    }

    public static int WriteActionResultResponse(Span<byte> frame, int route, ActionResultValue result)
    {
        WriteFrame(frame, route, ActionResultValue.Size);
        ActionResultValue.Write(frame.Slice(HeaderSize), result);
        return HeaderSize + ActionResultValue.Size;
    }

    public static int WriteServerStatusResponse(Span<byte> frame, int route, ServerStatusValue status)
    {
        WriteFrame(frame, route, ServerStatusValue.Size);
        ServerStatusValue.Write(frame.Slice(HeaderSize), status);
        return HeaderSize + ServerStatusValue.Size;
    }

    public static void WriteFrame(Span<byte> frame, int route, int payloadLength)
    {
        BinaryPrimitives.WriteInt32LittleEndian(frame, HeaderSize + payloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(frame.Slice(4), route);
    }
}
