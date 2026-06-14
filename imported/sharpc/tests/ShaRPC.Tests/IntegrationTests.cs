using Shared;
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;
using System.Net;
using Xunit;

namespace ShaRPC.Tests;

public class IntegrationTests : IAsyncLifetime
{
    private RpcHost? _host;
    private TcpTransport? _transport;
    private RpcPeer? _client;
    private IGameService? _gameService;

    public async Task InitializeAsync()
    {
        var serverTransport = new TcpServerTransport(IPAddress.Loopback, 0);
        var serializer = new MessagePackRpcSerializer();

        _host = RpcHost
            .Listen(serverTransport, serializer)
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        await _host.StartAsync();
        var port = serverTransport.LocalEndpoint?.Port ??
            throw new InvalidOperationException("TCP test server did not expose a bound port.");

        _transport = new TcpTransport("127.0.0.1", port);
        await _transport.ConnectAsync();
        _client = RpcPeer
            .Over(_transport.Connection!, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();
        _gameService = _client.GetGameService();
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }

        if (_transport != null)
        {
            await _transport.DisposeAsync();
        }

        if (_host != null)
        {
            await _host.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetServerStatus_ShouldReturnStatus()
    {
        // Act
        var status = await _gameService!.GetServerStatusAsync();

        // Assert
        Assert.NotNull(status);
        Assert.Equal("1.0.0-test", status.Version);
    }

    [Fact]
    public async Task RegisterPlayer_ShouldReturnPlayerState()
    {
        // Act
        var state = await _gameService!.RegisterPlayerAsync("TestPlayer");

        // Assert
        Assert.NotNull(state);
        Assert.Equal("TestPlayer", state.Name);
        Assert.NotEmpty(state.PlayerId);
        Assert.Equal(1, state.Level);
        Assert.Equal(100, state.Health);
    }

    [Fact]
    public async Task GetPlayerState_ShouldReturnState()
    {
        // Arrange
        var registered = await _gameService!.RegisterPlayerAsync("Player1");

        // Act
        var state = await _gameService.GetPlayerStateAsync(new PlayerId { Id = registered.PlayerId });

        // Assert
        Assert.NotNull(state);
        Assert.Equal(registered.PlayerId, state.PlayerId);
        Assert.Equal("Player1", state.Name);
    }

    [Fact]
    public async Task MovePlayer_ShouldUpdatePosition()
    {
        // Arrange
        var registered = await _gameService!.RegisterPlayerAsync("MovingPlayer");

        // Act
        var moveResult = await _gameService.MovePlayerAsync(new MoveRequest
        {
            PlayerId = registered.PlayerId,
            X = 100,
            Y = 50,
            Z = 200
        });

        var state = await _gameService.GetPlayerStateAsync(new PlayerId { Id = registered.PlayerId });

        // Assert
        Assert.True(moveResult.Success);
        Assert.Equal(100, state.PositionX);
        Assert.Equal(50, state.PositionY);
        Assert.Equal(200, state.PositionZ);
    }

    [Fact]
    public async Task PerformAction_ShouldSucceed()
    {
        // Arrange
        var registered = await _gameService!.RegisterPlayerAsync("ActionPlayer");

        // Act
        var result = await _gameService.PerformActionAsync(new ActionRequest
        {
            PlayerId = registered.PlayerId,
            ActionType = "Jump",
            TargetId = null
        });

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task MultipleConcurrentCalls_ShouldSucceed()
    {
        // Arrange
        var tasks = new List<Task<ServerStatus>>();

        // Act - Send multiple concurrent requests
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_gameService!.GetServerStatusAsync());
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, status =>
        {
            Assert.NotNull(status);
            Assert.Equal("1.0.0-test", status.Version);
        });
    }
}
