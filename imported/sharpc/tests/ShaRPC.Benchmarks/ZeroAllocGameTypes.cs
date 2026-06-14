using System.Buffers.Binary;

namespace ShaRPC.Benchmarks;

internal readonly struct RegisterPlayerRequest
{
    public const int Size = 4;

    public RegisterPlayerRequest(int nameToken) => NameToken = nameToken;

    public int NameToken { get; }

    public static RegisterPlayerRequest Read(ReadOnlySpan<byte> source) =>
        new(BinaryPrimitives.ReadInt32LittleEndian(source));

    public static void Write(Span<byte> destination, RegisterPlayerRequest value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.NameToken);
}

internal readonly struct GetPlayerStateRequest
{
    public const int Size = 4;

    public GetPlayerStateRequest(int playerId) => PlayerId = playerId;

    public int PlayerId { get; }

    public static GetPlayerStateRequest Read(ReadOnlySpan<byte> source) =>
        new(BinaryPrimitives.ReadInt32LittleEndian(source));

    public static void Write(Span<byte> destination, GetPlayerStateRequest value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.PlayerId);
}

internal readonly struct MovePlayerRequest
{
    public const int Size = 16;

    public MovePlayerRequest(int playerId, float x, float y, float z)
    {
        PlayerId = playerId;
        X = x;
        Y = y;
        Z = z;
    }

    public int PlayerId { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public static MovePlayerRequest Read(ReadOnlySpan<byte> source) =>
        new(
            BinaryPrimitives.ReadInt32LittleEndian(source),
            ZeroAllocScalars.ReadSingle(source.Slice(4)),
            ZeroAllocScalars.ReadSingle(source.Slice(8)),
            ZeroAllocScalars.ReadSingle(source.Slice(12)));

    public static void Write(Span<byte> destination, MovePlayerRequest value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.PlayerId);
        ZeroAllocScalars.WriteSingle(destination.Slice(4), value.X);
        ZeroAllocScalars.WriteSingle(destination.Slice(8), value.Y);
        ZeroAllocScalars.WriteSingle(destination.Slice(12), value.Z);
    }
}

internal readonly struct PerformActionRequest
{
    public const int Size = 12;

    public PerformActionRequest(int playerId, int actionToken, int targetToken)
    {
        PlayerId = playerId;
        ActionToken = actionToken;
        TargetToken = targetToken;
    }

    public int PlayerId { get; }
    public int ActionToken { get; }
    public int TargetToken { get; }

    public static PerformActionRequest Read(ReadOnlySpan<byte> source) =>
        new(
            BinaryPrimitives.ReadInt32LittleEndian(source),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4)),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8)));

    public static void Write(Span<byte> destination, PerformActionRequest value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.PlayerId);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), value.ActionToken);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), value.TargetToken);
    }
}

internal readonly struct HeartbeatRequest
{
    public const int Size = 12;

    public HeartbeatRequest(int playerId, long tick)
    {
        PlayerId = playerId;
        Tick = tick;
    }

    public int PlayerId { get; }
    public long Tick { get; }

    public static HeartbeatRequest Read(ReadOnlySpan<byte> source) =>
        new(
            BinaryPrimitives.ReadInt32LittleEndian(source),
            BinaryPrimitives.ReadInt64LittleEndian(source.Slice(4)));

    public static void Write(Span<byte> destination, HeartbeatRequest value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.PlayerId);
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(4), value.Tick);
    }
}
