using System.Buffers.Binary;

namespace ShaRPC.Benchmarks;

internal readonly struct PlayerStateValue
{
    public const int Size = 32;

    public PlayerStateValue(
        int playerId,
        int nameToken,
        int level,
        int health,
        int maxHealth,
        float x,
        float y,
        float z)
    {
        PlayerId = playerId;
        NameToken = nameToken;
        Level = level;
        Health = health;
        MaxHealth = maxHealth;
        X = x;
        Y = y;
        Z = z;
    }

    public int PlayerId { get; }
    public int NameToken { get; }
    public int Level { get; }
    public int Health { get; }
    public int MaxHealth { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public PlayerStateValue WithPosition(float x, float y, float z) =>
        new(PlayerId, NameToken, Level, Health, MaxHealth, x, y, z);

    public static void Write(Span<byte> destination, PlayerStateValue value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.PlayerId);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), value.NameToken);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), value.Level);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12), value.Health);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16), value.MaxHealth);
        ZeroAllocScalars.WriteSingle(destination.Slice(20), value.X);
        ZeroAllocScalars.WriteSingle(destination.Slice(24), value.Y);
        ZeroAllocScalars.WriteSingle(destination.Slice(28), value.Z);
    }
}

internal readonly struct ActionResultValue
{
    public const int Size = 8;

    public ActionResultValue(int success, int code)
    {
        Success = success;
        Code = code;
    }

    public int Success { get; }
    public int Code { get; }

    public static void Write(Span<byte> destination, ActionResultValue value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.Success);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), value.Code);
    }
}

internal readonly struct ServerStatusValue
{
    public const int Size = 20;

    public ServerStatusValue(int playerCount, long tick, int versionMajor, int versionMinor)
    {
        PlayerCount = playerCount;
        Tick = tick;
        VersionMajor = versionMajor;
        VersionMinor = versionMinor;
    }

    public int PlayerCount { get; }
    public long Tick { get; }
    public int VersionMajor { get; }
    public int VersionMinor { get; }

    public static void Write(Span<byte> destination, ServerStatusValue value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.PlayerCount);
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(4), value.Tick);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12), value.VersionMajor);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16), value.VersionMinor);
    }
}

internal static class ZeroAllocScalars
{
    public static float ReadSingle(ReadOnlySpan<byte> source) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));

    public static void WriteSingle(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));
}
