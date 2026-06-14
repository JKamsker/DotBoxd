using ShaRPC.Core;
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

        await using var leftPeer = RpcPeer
            .Over(leftConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .ProvideGameService(new TestGameService())
            .Start();

        await using var rightPeer = RpcPeer
            .Over(rightConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .ProvideGameService(new TestGameService())
            .Start();

        var rightService = leftPeer.GetGameService();
        var leftService = rightPeer.GetGameService();

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
