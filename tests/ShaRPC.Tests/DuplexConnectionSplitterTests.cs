using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using Xunit;

namespace ShaRPC.Tests;

public class DuplexConnectionSplitterTests
{
    [Fact]
    public async Task RoutesCancelFramesByHeaderWithoutRequiringEnvelope()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var splitter = new DuplexConnectionSplitter(serverConnection);
        splitter.Start();

        using var frame = MessageFramer.FrameToPayload(9, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
        await client.SendAsync(frame.Memory);

        using var routed = await splitter.ServerConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(frame.Memory.ToArray(), routed.Memory.ToArray());
    }
}
