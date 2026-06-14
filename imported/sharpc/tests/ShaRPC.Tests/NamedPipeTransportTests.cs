using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;
using Shared;
using Xunit;

namespace ShaRPC.Tests;

public sealed class NamedPipeTransportTests
{
    [Fact]
    public async Task NamedPipeConnection_RoundTripsFramedMessage()
    {
        var pipeName = CreatePipeName();
        await using var serverTransport = new NamedPipeServerTransport(pipeName);
        await serverTransport.StartAsync();

        var acceptTask = serverTransport.AcceptAsync();
        var clientTransport = new NamedPipeClientTransport(pipeName);
        await clientTransport.ConnectAsync();
        var serverConnection = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            var clientConnection = clientTransport.Connection
                ?? throw new InvalidOperationException("Client did not connect.");
            using var frame = MessageFramer.FrameToPayload(42, MessageType.Response, ReadOnlySpan<byte>.Empty);

            var receiveTask = serverConnection.ReceiveAsync();
            await clientConnection.SendAsync(frame.Memory).WaitAsync(TimeSpan.FromSeconds(5));
            using var received = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(frame.Memory.ToArray(), received.Memory.ToArray());
        }
        finally
        {
            await clientTransport.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await serverConnection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task NamedPipeTransport_RunsGeneratedRpcEndToEnd()
    {
        var pipeName = CreatePipeName();
        await using var host = RpcHost
            .Listen(new NamedPipeServerTransport(pipeName), new MessagePackRpcSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        await host.StartAsync();

        await using var clientTransport = new NamedPipeClientTransport(pipeName);
        await clientTransport.ConnectAsync();
        await using var client = RpcPeer
            .Over(
                clientTransport.Connection!,
                new MessagePackRpcSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var game = client.GetGameService();
        var status = await game.GetServerStatusAsync();

        Assert.Equal("1.0.0-test", status.Version);
    }

    private static string CreatePipeName() => "sharpc-tests-" + Guid.NewGuid().ToString("N");
}
