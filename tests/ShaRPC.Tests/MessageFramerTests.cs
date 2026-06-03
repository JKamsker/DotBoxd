using System.Buffers.Binary;
using ShaRPC.Core.Protocol;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public class MessageFramerTests
{
    [Fact]
    public void Frame_ShouldCreateValidFrame()
    {
        // Arrange
        var messageId = 42;
        var type = MessageType.Request;
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);
        var span = frame.Span;

        // Assert
        Assert.Equal(MessageFramer.HeaderSize + payload.Length, frame.Length);

        // Check total length (first 4 bytes)
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
        Assert.Equal(frame.Length, totalLength);

        // Check message ID (next 4 bytes)
        var extractedMessageId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        Assert.Equal(messageId, extractedMessageId);

        // Check message type (next 1 byte)
        Assert.Equal((byte)type, span[8]);

        // Check payload
        Assert.Equal(payload, frame.Memory.Slice(MessageFramer.HeaderSize).ToArray());
    }

    [Fact]
    public void Frame_WithEmptyPayload_ShouldCreateValidFrame()
    {
        // Arrange
        var messageId = 1;
        var type = MessageType.Response;
        var payload = Array.Empty<byte>();

        // Act
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);

        // Assert
        Assert.Equal(MessageFramer.HeaderSize, frame.Length);
    }

    [Fact]
    public void TryReadFrame_ShouldRoundTripEnvelopeAndPayload()
    {
        // Arrange
        var serializer = new MessagePackRpcSerializer();
        var messageId = 7;
        var type = MessageType.Request;
        var request = new RpcRequest
        {
            MessageId = messageId,
            ServiceName = "Calc",
            MethodName = "Add",
        };
        var payload = new byte[] { 9, 8, 7, 6 };
        using var frame = MessageFramer.FrameMessage(serializer, messageId, type, request, payload);

        // Act
        var ok = MessageFramer.TryReadFrame(
            frame.Memory,
            out var extractedId,
            out var extractedType,
            out var extractedEnvelope,
            out var extractedPayload);

        // Assert
        Assert.True(ok);
        Assert.Equal(messageId, extractedId);
        Assert.Equal(type, extractedType);
        Assert.Equal(payload, extractedPayload.ToArray());

        var roundTripped = serializer.Deserialize<RpcRequest>(extractedEnvelope);
        Assert.Equal(request.MessageId, roundTripped.MessageId);
        Assert.Equal(request.ServiceName, roundTripped.ServiceName);
        Assert.Equal(request.MethodName, roundTripped.MethodName);
    }

    [Fact]
    public void TryReadFrame_WithEmptyPayload_ShouldRoundTrip()
    {
        // Arrange
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest { MessageId = 3, ServiceName = "S", MethodName = "M" };
        using var frame = MessageFramer.FrameMessage(serializer, 3, MessageType.Request, request, ReadOnlySpan<byte>.Empty);

        // Act
        var ok = MessageFramer.TryReadFrame(
            frame.Memory,
            out var extractedId,
            out _,
            out var extractedEnvelope,
            out var extractedPayload);

        // Assert
        Assert.True(ok);
        Assert.Equal(3, extractedId);
        Assert.True(extractedPayload.IsEmpty);
        Assert.False(extractedEnvelope.IsEmpty);
    }

    [Fact]
    public void TryReadFrame_ParsingCost_DoesNotScaleWithPayloadSize()
    {
        // The un-nested wire format returns the trailing payload as a zero-copy slice of the frame
        // buffer, so parsing must not allocate anything proportional to payload size. If a per-message
        // copy regressed back in, parsing the large frame would allocate measurably more than the small.
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest { MessageId = 1, ServiceName = "S", MethodName = "M" };

        using var smallFrame = MessageFramer.FrameMessage(serializer, 1, MessageType.Request, request, new byte[16]);
        using var largeFrame = MessageFramer.FrameMessage(serializer, 1, MessageType.Request, request, new byte[256 * 1024]);

        var smallAllocated = MeasureParseAllocations(smallFrame.Memory);
        var largeAllocated = MeasureParseAllocations(largeFrame.Memory);

        Assert.Equal(smallAllocated, largeAllocated);
    }

    private static long MeasureParseAllocations(ReadOnlyMemory<byte> frame)
    {
        const int iterations = 1000;

        // Warm up so tiered JIT compilation does not allocate inside the measured window.
        long sink = 0;
        for (var i = 0; i < iterations; i++)
        {
            MessageFramer.TryReadFrame(frame, out _, out _, out var envelope, out var payload);
            sink += envelope.Length + payload.Length;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            MessageFramer.TryReadFrame(frame, out _, out _, out var envelope, out var payload);
            sink += envelope.Length + payload.Length;
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Keep `sink` observable so the calls cannot be optimized away.
        Assert.True(sink >= 0);
        return after - before;
    }

    [Fact]
    public async Task ReadMessageAsync_ShouldReadValidMessage()
    {
        // Arrange
        var messageId = 123;
        var type = MessageType.Request;
        var payload = new byte[] { 10, 20, 30 };
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);
        using var stream = new MemoryStream(frame.Memory.ToArray());

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        var msg = result.Value;
        try
        {
            Assert.Equal(messageId, msg.MessageId);
            Assert.Equal(type, msg.Type);
            Assert.Equal(payload, msg.Body.Memory.ToArray());
        }
        finally
        {
            msg.Body.Dispose();
        }
    }

    [Fact]
    public async Task ReadMessageAsync_WithEmptyStream_ShouldReturnNull()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteMessageAsync_ShouldWriteValidFrame()
    {
        // Arrange
        var messageId = 456;
        var type = MessageType.Error;
        var payload = new byte[] { 100, 200 };
        using var stream = new MemoryStream();

        // Act
        await MessageFramer.WriteMessageAsync(stream, messageId, type, payload);
        stream.Position = 0;
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        var msg = result.Value;
        try
        {
            Assert.Equal(messageId, msg.MessageId);
            Assert.Equal(type, msg.Type);
            Assert.Equal(payload, msg.Body.Memory.ToArray());
        }
        finally
        {
            msg.Body.Dispose();
        }
    }

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Error)]
    [InlineData(MessageType.Cancel)]
    public async Task RoundTrip_ShouldPreserveMessageType(MessageType type)
    {
        // Arrange
        var messageId = 789;
        var payload = new byte[] { 1 };
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);
        using var stream = new MemoryStream(frame.Memory.ToArray());

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        var msg = result.Value;
        try
        {
            Assert.Equal(type, msg.Type);
        }
        finally
        {
            msg.Body.Dispose();
        }
    }

    [Fact]
    public async Task ReadMessageAsync_WithLargePayload_ShouldSucceed()
    {
        // Arrange
        var messageId = 1;
        var type = MessageType.Request;
        var payload = new byte[10000];
        new Random(42).NextBytes(payload);
        using var frame = MessageFramer.FrameToPayload(messageId, type, payload);
        using var stream = new MemoryStream(frame.Memory.ToArray());

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        var msg = result.Value;
        try
        {
            Assert.Equal(payload, msg.Body.Memory.ToArray());
        }
        finally
        {
            msg.Body.Dispose();
        }
    }

    [Fact]
    public void TryReadFrame_WithTrailingBytes_ReturnsFalse()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest { MessageId = 5, ServiceName = "S", MethodName = "M" };
        using var frame = MessageFramer.FrameMessage(serializer, 5, MessageType.Request, request, new byte[] { 1, 2, 3 });

        // An exact-length buffer is one complete frame and must parse.
        Assert.True(MessageFramer.TryReadFrame(frame.Memory, out _, out _, out _, out _));

        // A buffer carrying the frame plus trailing garbage must be rejected rather than silently
        // accepted with the tail ignored — matters for custom IRpcChannel implementations.
        var withTail = new byte[frame.Length + 1];
        frame.Memory.Span.CopyTo(withTail);
        Assert.False(MessageFramer.TryReadFrame(withTail, out _, out _, out _, out _));
    }

    [Fact]
    public void TryReadFrameHeader_WithTrailingBytes_ReturnsFalse()
    {
        using var frame = MessageFramer.FrameToPayload(11, MessageType.Cancel, ReadOnlySpan<byte>.Empty);

        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out _, out _));

        var withTail = new byte[frame.Length + 2];
        frame.Memory.Span.CopyTo(withTail);
        Assert.False(MessageFramer.TryReadFrameHeader(withTail, out _, out _));
    }
}
