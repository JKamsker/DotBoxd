using ShaRPC.Core.Peer;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using Shared;
using Xunit;

namespace ShaRPC.Tests;

public class PeerIntegrationTests
{
    [Fact]
    public async Task Peers_CallEachOtherOverOneConnection()
    {
        var (leftConnection, rightConnection) = InMemoryPipe.CreateConnectionPair(writeChunkSize: 3);
        var serializer = new MessagePackRpcSerializer();

        await using var leftPeer = await ShaRpcPeer.StartAsync(
            leftConnection,
            serializer,
            builder => builder.AddDispatcher(ShaRpcGenerated.CreateDispatcher<IGameService>(new TestGameService())),
            new ShaRpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });

        await using var rightPeer = await ShaRpcPeer.StartAsync(
            rightConnection,
            serializer,
            builder => builder.AddDispatcher(ShaRpcGenerated.CreateDispatcher<IGameService>(new TestGameService())),
            new ShaRpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });

        var rightService = leftPeer.CreateProxy<IGameService>();
        var leftService = rightPeer.CreateProxy<IGameService>();

        var playerOnRight = await rightService.RegisterPlayerAsync("right-player");
        var playerOnLeft = await leftService.RegisterPlayerAsync("left-player");

        Assert.Equal("right-player", playerOnRight.Name);
        Assert.Equal("left-player", playerOnLeft.Name);

        var rightStatus = await rightService.GetServerStatusAsync();
        var leftStatus = await leftService.GetServerStatusAsync();

        Assert.Equal(1, rightStatus.PlayerCount);
        Assert.Equal(1, leftStatus.PlayerCount);
    }
}
