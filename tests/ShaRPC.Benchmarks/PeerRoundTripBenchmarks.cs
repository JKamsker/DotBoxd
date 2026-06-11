using BenchmarkDotNet.Attributes;
using ShaRPC.Core;
using ShaRPC.Serializers.MessagePack;
using Shared;

namespace ShaRPC.Benchmarks;

[MemoryDiagnoser]
public class PeerRoundTripBenchmarks
{
    private RpcPeer _leftPeer = null!;
    private RpcPeer _rightPeer = null!;
    private IGameService _service = null!;
    private readonly MoveRequest _request = new()
    {
        PlayerId = "player-1",
        X = 1,
        Y = 2,
        Z = 3
    };

    [GlobalSetup]
    public async Task Setup()
    {
        var (leftConnection, rightConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();

        _leftPeer = RpcPeer
            .Over(leftConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        _rightPeer = RpcPeer
            .Over(rightConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Provide<IGameService>(new BenchmarkGameService())
            .Start();

        _service = _leftPeer.Get<IGameService>();
        await _service.RegisterPlayerAsync("player-1").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _leftPeer.DisposeAsync().ConfigureAwait(false);
        await _rightPeer.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public Task<ActionResult> MovePlayerAsync() =>
        _service.MovePlayerAsync(_request);

    private sealed class BenchmarkGameService : IGameService
    {
        private readonly Dictionary<string, PlayerState> _players = new();

        public Task<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default) =>
            Task.FromResult(_players[playerId.Id]);

        public Task<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default)
        {
            var state = _players[request.PlayerId];
            state.PositionX = request.X;
            state.PositionY = request.Y;
            state.PositionZ = request.Z;
            return Task.FromResult(new ActionResult { Success = true, Message = "Moved" });
        }

        public Task<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ActionResult { Success = true, Message = request.ActionType });

        public Task<ServerStatus> GetServerStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new ServerStatus { PlayerCount = _players.Count, Version = "bench" });

        public Task<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default)
        {
            var state = new PlayerState
            {
                PlayerId = "player-1",
                Name = playerName,
                Level = 1,
                Health = 100,
                MaxHealth = 100
            };
            _players[state.PlayerId] = state;
            return Task.FromResult(state);
        }
    }
}
