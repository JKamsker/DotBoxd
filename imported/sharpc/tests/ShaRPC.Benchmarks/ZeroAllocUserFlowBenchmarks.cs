using BenchmarkDotNet.Attributes;

namespace ShaRPC.Benchmarks;

[MemoryDiagnoser]
public class ZeroAllocUserFlowBenchmarks
{
    private readonly ZeroAllocGameService _service = new();
    private readonly byte[] _registerFrame = new byte[ZeroAllocProtocol.HeaderSize + RegisterPlayerRequest.Size];
    private readonly byte[] _moveFrame = new byte[ZeroAllocProtocol.HeaderSize + MovePlayerRequest.Size];
    private readonly byte[] _statusFrame = new byte[ZeroAllocProtocol.HeaderSize];
    private readonly byte[] _getStateFrame = new byte[ZeroAllocProtocol.HeaderSize + GetPlayerStateRequest.Size];
    private readonly byte[] _missingStateFrame = new byte[ZeroAllocProtocol.HeaderSize + GetPlayerStateRequest.Size];
    private readonly byte[] _actionFrame = new byte[ZeroAllocProtocol.HeaderSize + PerformActionRequest.Size];
    private readonly byte[] _heartbeatFrame = new byte[ZeroAllocProtocol.HeaderSize + HeartbeatRequest.Size];
    private readonly byte[] _playerResponseFrame = new byte[ZeroAllocProtocol.HeaderSize + PlayerStateValue.Size];
    private readonly byte[] _actionResponseFrame = new byte[ZeroAllocProtocol.HeaderSize + ActionResultValue.Size];
    private readonly byte[] _statusResponseFrame = new byte[ZeroAllocProtocol.HeaderSize + ServerStatusValue.Size];
    private readonly byte[] _ackResponseFrame = new byte[ZeroAllocProtocol.HeaderSize];
    private readonly byte[] _sessionResponseFrames = new byte[
        (ZeroAllocProtocol.HeaderSize + PlayerStateValue.Size) +
        (ZeroAllocProtocol.HeaderSize + PlayerStateValue.Size) +
        (ZeroAllocProtocol.HeaderSize + ActionResultValue.Size) +
        (ZeroAllocProtocol.HeaderSize + ActionResultValue.Size) +
        (ZeroAllocProtocol.HeaderSize + ServerStatusValue.Size) +
        ZeroAllocProtocol.HeaderSize];

    [GlobalSetup]
    public void Setup()
    {
        var player = _service.Register(new RegisterPlayerRequest(nameToken: 1001));
        Write(_registerFrame, ZeroAllocProtocol.RegisterRoute, new RegisterPlayerRequest(1001));
        Write(_moveFrame, ZeroAllocProtocol.MoveRoute, new MovePlayerRequest(player.PlayerId, 10, 20, 30));
        Write(_getStateFrame, ZeroAllocProtocol.GetStateRoute, new GetPlayerStateRequest(player.PlayerId));
        Write(_missingStateFrame, ZeroAllocProtocol.GetStateRoute, new GetPlayerStateRequest(404));
        Write(_actionFrame, ZeroAllocProtocol.ActionRoute, new PerformActionRequest(player.PlayerId, 7, 99));
        Write(_heartbeatFrame, ZeroAllocProtocol.HeartbeatRoute, new HeartbeatRequest(player.PlayerId, 123));
        ZeroAllocProtocol.WriteFrame(_statusFrame, ZeroAllocProtocol.StatusRoute, payloadLength: 0);
    }

    [Benchmark]
    public int RegisterPlayerFlow()
    {
        var request = RegisterPlayerRequest.Read(
            ZeroAllocProtocol.ReadPayload(_registerFrame, ZeroAllocProtocol.RegisterRoute));
        return ZeroAllocProtocol.WritePlayerStateResponse(
            _playerResponseFrame,
            ZeroAllocProtocol.RegisterRoute,
            _service.Register(request));
    }

    [Benchmark]
    public int GetPlayerStateFlow()
    {
        var request = GetPlayerStateRequest.Read(
            ZeroAllocProtocol.ReadPayload(_getStateFrame, ZeroAllocProtocol.GetStateRoute));
        _service.TryGetPlayerState(request, out var player);
        return ZeroAllocProtocol.WritePlayerStateResponse(
            _playerResponseFrame,
            ZeroAllocProtocol.GetStateRoute,
            player);
    }

    [Benchmark]
    public int MovePlayerFlow()
    {
        var request = MovePlayerRequest.Read(
            ZeroAllocProtocol.ReadPayload(_moveFrame, ZeroAllocProtocol.MoveRoute));
        return ZeroAllocProtocol.WriteActionResultResponse(
            _actionResponseFrame,
            ZeroAllocProtocol.MoveRoute,
            _service.Move(request));
    }

    [Benchmark]
    public int PerformActionFlow()
    {
        var request = PerformActionRequest.Read(
            ZeroAllocProtocol.ReadPayload(_actionFrame, ZeroAllocProtocol.ActionRoute));
        return ZeroAllocProtocol.WriteActionResultResponse(
            _actionResponseFrame,
            ZeroAllocProtocol.ActionRoute,
            _service.PerformAction(request));
    }

    [Benchmark]
    public int MissingPlayerFailureFlow()
    {
        var request = GetPlayerStateRequest.Read(
            ZeroAllocProtocol.ReadPayload(_missingStateFrame, ZeroAllocProtocol.GetStateRoute));
        var result = _service.TryGetPlayerState(request, out _);
        return ZeroAllocProtocol.WriteActionResultResponse(
            _actionResponseFrame,
            ZeroAllocProtocol.GetStateRoute,
            result);
    }

    [Benchmark]
    public int VoidHeartbeatFlow()
    {
        var request = HeartbeatRequest.Read(
            ZeroAllocProtocol.ReadPayload(_heartbeatFrame, ZeroAllocProtocol.HeartbeatRoute));
        _service.Heartbeat(request);
        return ZeroAllocProtocol.WriteAckResponse(_ackResponseFrame, ZeroAllocProtocol.HeartbeatRoute);
    }

    [Benchmark]
    public int FullGameplaySessionFlow()
    {
        var output = _sessionResponseFrames.AsSpan();
        var written = Register(output);
        written += GetState(output.Slice(written));
        written += Move(output.Slice(written));
        written += PerformAction(output.Slice(written));
        written += WriteStatus(output.Slice(written));
        written += Heartbeat(output.Slice(written));
        return written;
    }

    private int Register(Span<byte> output)
    {
        var request = RegisterPlayerRequest.Read(
            ZeroAllocProtocol.ReadPayload(_registerFrame, ZeroAllocProtocol.RegisterRoute));
        return ZeroAllocProtocol.WritePlayerStateResponse(
            output,
            ZeroAllocProtocol.RegisterRoute,
            _service.Register(request));
    }

    private int GetState(Span<byte> output)
    {
        var request = GetPlayerStateRequest.Read(
            ZeroAllocProtocol.ReadPayload(_getStateFrame, ZeroAllocProtocol.GetStateRoute));
        _service.TryGetPlayerState(request, out var player);
        return ZeroAllocProtocol.WritePlayerStateResponse(output, ZeroAllocProtocol.GetStateRoute, player);
    }

    private int Move(Span<byte> output)
    {
        var request = MovePlayerRequest.Read(
            ZeroAllocProtocol.ReadPayload(_moveFrame, ZeroAllocProtocol.MoveRoute));
        return ZeroAllocProtocol.WriteActionResultResponse(output, ZeroAllocProtocol.MoveRoute, _service.Move(request));
    }

    private int PerformAction(Span<byte> output)
    {
        var request = PerformActionRequest.Read(
            ZeroAllocProtocol.ReadPayload(_actionFrame, ZeroAllocProtocol.ActionRoute));
        return ZeroAllocProtocol.WriteActionResultResponse(
            output,
            ZeroAllocProtocol.ActionRoute,
            _service.PerformAction(request));
    }

    private int WriteStatus(Span<byte> output)
    {
        ZeroAllocProtocol.ReadPayload(_statusFrame, ZeroAllocProtocol.StatusRoute);
        return ZeroAllocProtocol.WriteServerStatusResponse(
            output,
            ZeroAllocProtocol.StatusRoute,
            _service.GetStatus());
    }

    private int Heartbeat(Span<byte> output)
    {
        var request = HeartbeatRequest.Read(
            ZeroAllocProtocol.ReadPayload(_heartbeatFrame, ZeroAllocProtocol.HeartbeatRoute));
        _service.Heartbeat(request);
        return ZeroAllocProtocol.WriteAckResponse(output, ZeroAllocProtocol.HeartbeatRoute);
    }

    private static void Write(Span<byte> frame, int route, RegisterPlayerRequest request)
    {
        ZeroAllocProtocol.WriteFrame(frame, route, RegisterPlayerRequest.Size);
        RegisterPlayerRequest.Write(frame.Slice(ZeroAllocProtocol.HeaderSize), request);
    }

    private static void Write(Span<byte> frame, int route, MovePlayerRequest request)
    {
        ZeroAllocProtocol.WriteFrame(frame, route, MovePlayerRequest.Size);
        MovePlayerRequest.Write(frame.Slice(ZeroAllocProtocol.HeaderSize), request);
    }

    private static void Write(Span<byte> frame, int route, GetPlayerStateRequest request)
    {
        ZeroAllocProtocol.WriteFrame(frame, route, GetPlayerStateRequest.Size);
        GetPlayerStateRequest.Write(frame.Slice(ZeroAllocProtocol.HeaderSize), request);
    }

    private static void Write(Span<byte> frame, int route, PerformActionRequest request)
    {
        ZeroAllocProtocol.WriteFrame(frame, route, PerformActionRequest.Size);
        PerformActionRequest.Write(frame.Slice(ZeroAllocProtocol.HeaderSize), request);
    }

    private static void Write(Span<byte> frame, int route, HeartbeatRequest request)
    {
        ZeroAllocProtocol.WriteFrame(frame, route, HeartbeatRequest.Size);
        HeartbeatRequest.Write(frame.Slice(ZeroAllocProtocol.HeaderSize), request);
    }
}
