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
    private IValueTaskGameService _service = null!;
    private readonly MoveRequest _request = new()
    {
        PlayerId = "player-1",
        X = 1,
        Y = 2,
        Z = 3
    };

    // Combined hot-path profile: no peer timeouts, client pooled ValueTask<T> invocations,
    // no non-streaming inbound cancellation, and immediate server dispatch.
    [Params(false, true)]
    public bool EndToEndLowAllocationProfile { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var (leftConnection, rightConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();

        _leftPeer = RpcPeer
            .Over(leftConnection, serializer, CreateOptionsForClient())
            .Start();

        _rightPeer = RpcPeer
            .Over(
                rightConnection,
                serializer,
                CreateOptionsForServer())
            .Provide<IValueTaskGameService>(new BenchmarkGameService())
            .Start();

        _service = _leftPeer.Get<IValueTaskGameService>();
        await _service.RegisterPlayerAsync("player-1").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _leftPeer.DisposeAsync().ConfigureAwait(false);
        await _rightPeer.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public ValueTask<ActionResult> MovePlayerAsync() =>
        _service.MovePlayerAsync(_request);

    private RpcPeerOptions CreateOptionsForClient() =>
        new()
        {
            RequestTimeout = EndToEndLowAllocationProfile
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromSeconds(5),
            EnableLowAllocationValueTaskInvocations = EndToEndLowAllocationProfile,
        };

    private RpcPeerOptions CreateOptionsForServer()
    {
        if (!EndToEndLowAllocationProfile)
        {
            return new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) };
        }

        return new RpcPeerOptions
        {
            DisableInboundRequestCancellation = true,
            InboundQueueCapacity = null,
            RequestTimeout = Timeout.InfiniteTimeSpan,
        };
    }

    private sealed class BenchmarkGameService : IValueTaskGameService
    {
        private static readonly ActionResult MoveResult = new() { Success = true, Message = "Moved" };

        private readonly Dictionary<string, PlayerState> _players = new();

        public ValueTask<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default) =>
            new(_players[playerId.Id]);

        public ValueTask<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default)
        {
            var playerId = request.PlayerId
                ?? throw new InvalidOperationException("MoveRequest.PlayerId must not be null.");
            var state = _players[playerId];
            state.PositionX = request.X;
            state.PositionY = request.Y;
            state.PositionZ = request.Z;
            return new ValueTask<ActionResult>(MoveResult);
        }

        public ValueTask<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default) =>
            new(new ActionResult { Success = true, Message = request.ActionType });

        public ValueTask<ServerStatus> GetServerStatusAsync(CancellationToken ct = default) =>
            new(new ServerStatus { PlayerCount = _players.Count, Version = "bench" });

        public ValueTask<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default)
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
            return new ValueTask<PlayerState>(state);
        }
    }
}
