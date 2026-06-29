using System.Buffers.Binary;
using System.IO.Pipes;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public class StreamConnectionTests
{
    [Fact]
    public async Task NamedPipeStream_RoundTripsCompleteDotBoxDRpcFrame()
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
            using var received = await receiveTask.WaitAsync(TimeSpan.FromSeconds(30));

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
    public async Task ReceiveAsync_LengthPrefixThenEof_ThrowsInvalidDataException()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, MessageFramer.HeaderSize + 1);
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

    [Fact]
    public async Task SendAsync_RejectsUndefinedMessageType()
    {
        var bytes = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, bytes.Length);
        bytes[8] = 0x7F;
        var stream = new MemoryStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => connection.SendAsync(bytes));

        Assert.Contains("message type", ex.Message);
        Assert.Empty(stream.ToArray());
    }

    [Fact]
    public async Task FrameChannel_SendsPooledWriterAndReceivesRpcFrame()
    {
        var output = new MemoryStream();
        await using var sender = new StreamConnection(output, ownsStream: false);
        var sendChannel = Assert.IsAssignableFrom<IRpcFrameChannel>(sender);
        var frame = new PooledBufferWriter();
        MessageFramer.WriteFrame(frame, 7, MessageType.Request, ReadOnlySpan<byte>.Empty);

        await sendChannel.SendFrameValueAsync(frame);

        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);
        await using var receiver = new StreamConnection(
            new MemoryStream(output.ToArray()),
            ownsStream: false);
        var receiveChannel = Assert.IsAssignableFrom<IRpcFrameChannel>(receiver);
        using var received = await receiveChannel.ReceiveFrameValueAsync();

        Assert.True(MessageFramer.TryReadFrameHeader(received.Memory, out var messageId, out var type));
        Assert.Equal(7, messageId);
        Assert.Equal(MessageType.Request, type);
    }
}
