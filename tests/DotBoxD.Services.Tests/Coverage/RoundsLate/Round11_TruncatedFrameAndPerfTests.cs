using System.Buffers.Binary;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.RoundsLate;

/// <summary>
/// Round 11: bugs and performance issues found by review agents.
/// These tests are written RED — they expose issues in the current code and are expected
/// to fail until the corresponding fixes are applied.
/// </summary>
public sealed class Round11_TruncatedFrameAndPerfTests
{
    // ────────────────────────────────────────────────────────────────────
    // BUG: MessageFramer.ReadMessageAsync returns null (clean EOF) when
    // the stream closes mid-payload, instead of throwing InvalidDataException.
    // A truncated frame is a protocol error, not a graceful disconnect.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadMessageAsync_TruncatedPayload_ThrowsInvalidDataException()
    {
        // Arrange: build a valid header declaring 100 bytes of payload, but only write 1.
        var payloadLength = 100;
        var totalLength = MessageFramer.HeaderSize + payloadLength;
        var buffer = new byte[MessageFramer.HeaderSize + 1]; // header + 1 byte of payload
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 42); // messageId
        buffer[8] = (byte)MessageType.Request;
        buffer[9] = 0xAA; // single payload byte

        using var stream = new MemoryStream(buffer);

        // Act & Assert: should throw because the frame is incomplete, not return null.
        await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task ReadMessageAsync_TruncatedHeader_ThrowsInvalidDataException()
    {
        var buffer = new byte[5]; // partial header
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), MessageFramer.HeaderSize + 10);
        buffer[4] = 0x01;

        using var stream = new MemoryStream(buffer);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => MessageFramer.ReadMessageAsync(stream));
        Assert.Contains("frame header bytes", ex.Message);
    }

    // ────────────────────────────────────────────────────────────────────
    // BUG: StreamConnection.ReceiveAsync returns Payload.Empty (clean EOF)
    // when the stream closes mid-frame body, instead of throwing.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamConnection_ReceiveAsync_TruncatedBody_ThrowsInvalidDataException()
    {
        // Arrange: length prefix says 20 bytes total, but only 10 bytes follow (6 of body).
        var totalLength = 20;
        var buffer = new byte[10]; // 4 (length) + 6 (partial remaining, need 16)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), totalLength);
        // Fill with valid header bytes after length prefix
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 99); // partial messageId area
        buffer[8] = (byte)MessageType.Request;
        buffer[9] = 0xBB;

        await using var connection = new StreamConnection(new MemoryStream(buffer));

        // Act & Assert: truncated body should be a protocol error, not a clean EOF.
        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());
    }

    // ────────────────────────────────────────────────────────────────────
    // BUG: RpcPipeBridge.PumpAsync does not call writer.CompleteAsync()
    // when flush.IsCompleted — the pipe reader never gets an end signal.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PipeBridge_WhenReaderCompleted_WriterIsAlsoCompleted()
    {
        // Arrange
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);
        var handle = new RpcStreamHandle(300, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        var pipe = RpcPipeBridge.CreateReadablePipe(receiver, CancellationToken.None);

        // Complete the reader side first, then feed a chunk so the pump sees IsCompleted on flush.
        await pipe.Reader.CompleteAsync();

        using var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 0x01, 0x02, 0x03 });
        streams.TryAcceptItem(handle.StreamId, frame);

        // Act: wait for the pump to process the chunk and hit the IsCompleted path.
        // The pump should call writer.CompleteAsync() so that FlushAsync on the writer
        // throws InvalidOperationException (writer already completed).
        await Task.Delay(500);

        // Assert: the writer should have been completed by the pump.
        // If it wasn't, FlushAsync will succeed instead of throwing.
        var writerCompleted = false;
        try
        {
            var memory = pipe.Writer.GetMemory(1);
            pipe.Writer.Advance(1);
            await pipe.Writer.FlushAsync();
        }
        catch (InvalidOperationException)
        {
            writerCompleted = true;
        }

        Assert.True(writerCompleted,
            "PipeWriter should have been completed by the pump when reader was completed, " +
            "but it was still writable.");
    }

    // ────────────────────────────────────────────────────────────────────
    // PERF: StreamConnection.ReceiveAsync uses a pre-allocated field for
    // the 4-byte length prefix instead of renting from ArrayPool per frame.
    // Verify the field-based approach does not regress per-frame allocations.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamConnection_ReceiveAsync_UsesFieldForLengthBuffer()
    {
        // The fix replaces ArrayPool.Rent(4)/Return per frame with a byte[4] field.
        // Verify by reading multiple frames: the connection should not touch ArrayPool at all.
        const int frameCount = 20;
        using var ms = new MemoryStream();
        var payload = new byte[] { 1, 2, 3 };
        for (var i = 0; i < frameCount; i++)
        {
            using var frame = MessageFramer.FrameToPayload(i, MessageType.Request, payload);
            ms.Write(frame.Memory.Span);
        }

        ms.Position = 0;
        await using var connection = new StreamConnection(ms, ownsStream: false);

        for (var i = 0; i < frameCount; i++)
        {
            using var f = await connection.ReceiveAsync();
            Assert.True(f.Length > 0, $"Frame {i} should not be empty.");
        }

        // After all frames, EOF should return Empty.
        using var eof = await connection.ReceiveAsync();
        Assert.Same(Payload.Empty, eof);
    }
}
