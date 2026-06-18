using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class TcpReceivePrefixBufferTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ReceiveAsync_reads_many_small_frames_with_stable_prefix_buffer()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConnection = await acceptTask.WaitAsync(Timeout);
        var frameChannel = Assert.IsAssignableFrom<IRpcFrameChannel>(serverConnection);

        var stream = rawClient.GetStream();
        var body = new byte[] { 1, 2, 3 };
        const int frameCount = 20;
        for (var i = 0; i < frameCount; i++)
        {
            using var frame = MessageFramer.FrameToPayload(i, MessageType.Request, body);
            await stream.WriteAsync(frame.Memory).AsTask().WaitAsync(Timeout);
        }

        await stream.FlushAsync().WaitAsync(Timeout);

        for (var i = 0; i < frameCount; i++)
        {
            using var received = await frameChannel.ReceiveFrameValueAsync().AsTask().WaitAsync(Timeout);
            AssertFrame(received, i);
        }

        rawClient.Client.Shutdown(SocketShutdown.Send);
        using var eof = await serverConnection.ReceiveAsync().WaitAsync(Timeout);
        Assert.Same(Payload.Empty, eof);
    }

    private static void AssertFrame(RpcFrame frame, int expectedMessageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var messageId, out var type));
        Assert.Equal(expectedMessageId, messageId);
        Assert.Equal(MessageType.Request, type);
    }
}
