using System.Buffers.Binary;
using System.IO.Pipes;
using DotBoxd.Services.Buffers;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Transport;
using Xunit;

namespace DotBoxd.Services.Tests;

public class StreamConnectionTests
{
    [Fact]
    public async Task NamedPipeStream_RoundTripsCompleteDotBoxdRpcFrame()
    {
        var pipeName = "dotboxd-test-" + Guid.NewGuid().ToString("N");
        var serverPipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var clientPipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        var waitForServer = serverPipe.WaitForConnectionAsync();
        await clientPipe.ConnectAsync(5_000);
        await waitForServer;

        await using var server = new StreamConnection(serverPipe, "server-pipe", ownsStream: false);
        await using var client = new StreamConnection(clientPipe, "client-pipe", ownsStream: false);
        using var frame = MessageFramer.FrameToPayload(42, MessageType.Cancel, ReadOnlySpan<byte>.Empty);

        try
        {
            var receiveTask = server.ReceiveAsync();
            await client.SendAsync(frame.Memory);
            using var received = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(frame.Memory.ToArray(), received.Memory.ToArray());
        }
        finally
        {
            clientPipe.Dispose();
            serverPipe.Dispose();
        }
    }

    [Fact]
    public async Task ReceiveAsync_ReturnsEmptyPayloadOnCleanEof()
    {
        await using var connection = new StreamConnection(new MemoryStream(Array.Empty<byte>()));

        using var received = await connection.ReceiveAsync();

        Assert.Same(Payload.Empty, received);
    }

    [Fact]
    public async Task ReceiveAsync_RejectsInvalidFrameLength()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, MessageFramer.HeaderSize - 1);
        await using var connection = new StreamConnection(new MemoryStream(bytes));

        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());
    }

    [Fact]
    public async Task SendAsync_RejectsMismatchedFrameLength()
    {
        var bytes = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, bytes.Length + 1);
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => connection.SendAsync(bytes));
    }
}
