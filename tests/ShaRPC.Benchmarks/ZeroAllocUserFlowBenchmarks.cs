using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;

namespace ShaRPC.Benchmarks;

[MemoryDiagnoser]
public class ZeroAllocUserFlowBenchmarks
{
    private const int HeaderSize = 8;
    private const int RegisterRoute = 1;
    private const int MoveRoute = 2;
    private const int StatusRoute = 3;

    private readonly FastGameService _service = new();
    private readonly byte[] _registerFrame = new byte[HeaderSize + RegisterPlayerRequest.Size];
    private readonly byte[] _moveFrame = new byte[HeaderSize + MovePlayerRequest.Size];
    private readonly byte[] _statusFrame = new byte[HeaderSize];
    private readonly byte[] _singleResponseFrame = new byte[HeaderSize + PlayerStateValue.Size];
    private readonly byte[] _sessionResponseFrames = new byte[
        (HeaderSize + PlayerStateValue.Size) +
        (HeaderSize + ActionResultValue.Size) +
        (HeaderSize + ServerStatusValue.Size)];

    [GlobalSetup]
    public void Setup()
    {
        var player = _service.Register(new RegisterPlayerRequest(nameToken: 1001));
        WriteFrame(_registerFrame, RegisterRoute, RegisterPlayerRequest.Size);
        RegisterPlayerRequest.Write(_registerFrame.AsSpan(HeaderSize), new RegisterPlayerRequest(1001));

        WriteFrame(_moveFrame, MoveRoute, MovePlayerRequest.Size);
        MovePlayerRequest.Write(_moveFrame.AsSpan(HeaderSize), new MovePlayerRequest(player.PlayerId, 10, 20, 30));

        WriteFrame(_statusFrame, StatusRoute, payloadLength: 0);
    }

    [Benchmark]
    public int RegisterPlayerFlow()
    {
        var request = RegisterPlayerRequest.Read(ReadPayload(_registerFrame, RegisterRoute));
        var player = _service.Register(request);
        return WritePlayerStateResponse(_singleResponseFrame, RegisterRoute, player);
    }

    [Benchmark]
    public int MovePlayerFlow()
    {
        var request = MovePlayerRequest.Read(ReadPayload(_moveFrame, MoveRoute));
        var result = _service.Move(request);
        return WriteActionResultResponse(_singleResponseFrame, MoveRoute, result);
    }

    [Benchmark]
    public int RegisterMoveStatusSessionFlow()
    {
        var output = _sessionResponseFrames.AsSpan();

        var register = RegisterPlayerRequest.Read(ReadPayload(_registerFrame, RegisterRoute));
        var player = _service.Register(register);
        var written = WritePlayerStateResponse(output, RegisterRoute, player);

        var move = MovePlayerRequest.Read(ReadPayload(_moveFrame, MoveRoute));
        var moveResult = _service.Move(move);
        written += WriteActionResultResponse(output.Slice(written), MoveRoute, moveResult);

        ReadPayload(_statusFrame, StatusRoute);
        var status = _service.GetStatus();
        written += WriteServerStatusResponse(output.Slice(written), StatusRoute, status);
        return written;
    }

    private static ReadOnlySpan<byte> ReadPayload(ReadOnlySpan<byte> frame, int expectedRoute)
    {
        var length = BinaryPrimitives.ReadInt32LittleEndian(frame);
        var route = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(4));
        if (length != frame.Length || route != expectedRoute)
        {
            throw new InvalidOperationException("Invalid benchmark frame.");
        }

        return frame.Slice(HeaderSize);
    }

    private static int WritePlayerStateResponse(Span<byte> frame, int route, PlayerStateValue player)
    {
        WriteFrame(frame, route, PlayerStateValue.Size);
        PlayerStateValue.Write(frame.Slice(HeaderSize), player);
        return HeaderSize + PlayerStateValue.Size;
    }

    private static int WriteActionResultResponse(Span<byte> frame, int route, ActionResultValue result)
    {
        WriteFrame(frame, route, ActionResultValue.Size);
        ActionResultValue.Write(frame.Slice(HeaderSize), result);
        return HeaderSize + ActionResultValue.Size;
    }

    private static int WriteServerStatusResponse(Span<byte> frame, int route, ServerStatusValue status)
    {
        WriteFrame(frame, route, ServerStatusValue.Size);
        ServerStatusValue.Write(frame.Slice(HeaderSize), status);
        return HeaderSize + ServerStatusValue.Size;
    }

    private static void WriteFrame(Span<byte> frame, int route, int payloadLength)
    {
        BinaryPrimitives.WriteInt32LittleEndian(frame, HeaderSize + payloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(frame.Slice(4), route);
    }

    private sealed class FastGameService
    {
        private PlayerStateValue _player;
        private int _registered;

        public PlayerStateValue Register(RegisterPlayerRequest request)
        {
            _registered = 1;
            _player = new PlayerStateValue(1, request.NameToken, 1, 100, 100, 0, 0, 0);
            return _player;
        }

        public ActionResultValue Move(MovePlayerRequest request)
        {
            if (_registered == 0 || request.PlayerId != _player.PlayerId)
            {
                return new ActionResultValue(success: 0, code: 404);
            }

            _player = _player.WithPosition(request.X, request.Y, request.Z);
            return new ActionResultValue(success: 1, code: 0);
        }

        public ServerStatusValue GetStatus() =>
            new(_registered, tick: 1, versionMajor: 1, versionMinor: 0);
    }

    private readonly struct RegisterPlayerRequest
    {
        public const int Size = 4;

        public RegisterPlayerRequest(int nameToken) => NameToken = nameToken;

        public int NameToken { get; }

        public static RegisterPlayerRequest Read(ReadOnlySpan<byte> source) =>
            new(BinaryPrimitives.ReadInt32LittleEndian(source));

        public static void Write(Span<byte> destination, RegisterPlayerRequest value) =>
            BinaryPrimitives.WriteInt32LittleEndian(destination, value.NameToken);
    }

    private readonly struct MovePlayerRequest
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
                ReadSingle(source.Slice(4)),
                ReadSingle(source.Slice(8)),
                ReadSingle(source.Slice(12)));

        public static void Write(Span<byte> destination, MovePlayerRequest value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value.PlayerId);
            WriteSingle(destination.Slice(4), value.X);
            WriteSingle(destination.Slice(8), value.Y);
            WriteSingle(destination.Slice(12), value.Z);
        }
    }

    private readonly struct PlayerStateValue
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
            WriteSingle(destination.Slice(20), value.X);
            WriteSingle(destination.Slice(24), value.Y);
            WriteSingle(destination.Slice(28), value.Z);
        }
    }

    private readonly struct ActionResultValue
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

    private readonly struct ServerStatusValue
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

    private static float ReadSingle(ReadOnlySpan<byte> source) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));

    private static void WriteSingle(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));
}
