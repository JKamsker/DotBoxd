using System.Buffers.Binary;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

/// <summary>
/// Frame-level behavioral coverage for <see cref="StreamConnection"/> over plain in-memory streams.
/// A non-pipe stream is used deliberately so the <c>FlushAsync</c> branch of the send path and the
/// length-prefix read/validate/reassembly logic of the receive path are exercised exactly as they
/// would be over a real socket/pipe, but deterministically and without sockets.
/// </summary>
public sealed class StreamConnectionCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Constructor_Throws_WhenStreamNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new StreamConnection(stream: null!));
        Assert.Equal("stream", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxMessageSizeBelowHeader()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new StreamConnection(new MemoryStream(), maxMessageSize: MessageFramer.HeaderSize - 1));
        Assert.Equal("maxMessageSize", ex.ParamName);
    }

    [Fact]
    public async Task RemoteEndpoint_DefaultsToStream_WhenNotProvided()
    {
        await using var connection = new StreamConnection(new MemoryStream());

        Assert.Equal("stream", connection.RemoteEndpoint);
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public async Task RemoteEndpoint_UsesProvidedValue()
    {
        await using var connection = new StreamConnection(new MemoryStream(), "endpoint://x");

        Assert.Equal("endpoint://x", connection.RemoteEndpoint);
    }

    [Fact]
    public async Task SendAsync_WritesFrameAndFlushes_OnNonPipeStream()
    {
        var stream = new MemoryStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var frame = MessageFramer.FrameToPayload(11, MessageType.Request, new byte[] { 5, 6, 7 });

        await connection.SendAsync(frame.Memory).WaitAsync(Timeout);

        Assert.Equal(frame.Memory.ToArray(), stream.ToArray());
        stream.Dispose();
    }

    [Fact]
    public async Task SendAsync_Throws_AfterDispose()
    {
        var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        using var frame = MessageFramer.FrameToPayload(1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        await connection.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.SendAsync(frame.Memory));
    }

    [Fact]
    public async Task SendAsync_RejectsFrame_ExceedingMaxMessageSize()
    {
        // A frame whose declared length is within the wire format but above this connection's
        // configured maximum must be rejected locally before any bytes are written.
        var stream = new MemoryStream();
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            maxMessageSize: MessageFramer.HeaderSize + 1);

        using var frame = MessageFramer.FrameToPayload(
            1,
            MessageType.Request,
            new byte[10]); // body 10 -> total well over the limit

        await Assert.ThrowsAsync<InvalidDataException>(() => connection.SendAsync(frame.Memory));
        Assert.Empty(stream.ToArray());
        stream.Dispose();
    }

    [Fact]
    public async Task ReceiveAsync_ReturnsCompleteFrame_FromSingleRead()
    {
        using var frame = MessageFramer.FrameToPayload(99, MessageType.Response, new byte[] { 1, 2, 3, 4 });
        var stream = new MemoryStream(frame.Memory.ToArray());
        await using var connection = new StreamConnection(stream, ownsStream: false);

        using var received = await connection.ReceiveAsync().WaitAsync(Timeout);

        Assert.Equal(frame.Memory.ToArray(), received.Memory.ToArray());
        stream.Dispose();
    }

    [Fact]
    public async Task ReceiveAsync_ReassemblesFrame_FromPartialReads()
    {
        // A stream that yields a single byte per ReadAsync forces ReadExactAsync to loop, exercising
        // the multi-read reassembly path for both the 4-byte prefix and the body.
        using var frame = MessageFramer.FrameToPayload(3, MessageType.Request, new byte[] { 10, 20, 30 });
        await using var stream = new DripStream(frame.Memory.ToArray(), bytesPerRead: 1);
        await using var connection = new StreamConnection(stream, ownsStream: false);

        using var received = await connection.ReceiveAsync().WaitAsync(Timeout);

        Assert.Equal(frame.Memory.ToArray(), received.Memory.ToArray());
    }

    [Fact]
    public async Task ReceiveAsync_ReturnsEmpty_OnImmediateEof()
    {
        await using var connection = new StreamConnection(new MemoryStream(Array.Empty<byte>()));

        using var received = await connection.ReceiveAsync().WaitAsync(Timeout);

        Assert.Same(Payload.Empty, received);
    }

    [Fact]
    public async Task ReceiveAsync_Throws_WhenPrefixTruncated()
    {
        await using var connection = new StreamConnection(new MemoryStream(new byte[] { 1, 2 }));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => connection.ReceiveAsync().WaitAsync(Timeout));
        Assert.Contains("frame length bytes", ex.Message);
    }

    [Fact]
    public async Task ReceiveAsync_Throws_WhenBodyTruncated()
    {
        // Valid length prefix promises a larger frame than the stream actually delivers.
        // A truncated mid-frame body is a protocol error, not a graceful disconnect.
        var total = MessageFramer.HeaderSize + 8;
        var bytes = new byte[MessageFramer.HeaderSize]; // prefix + header only, body missing
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), total);
        await using var connection = new StreamConnection(new MemoryStream(bytes));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => connection.ReceiveAsync().WaitAsync(Timeout));
    }

    [Fact]
    public async Task ReceiveAsync_Throws_WhenTotalLengthEqualsFour()
    {
        // A frame whose declared total length is exactly 4 (only the prefix) must be rejected:
        // it is below the DotBoxD header size, so incoming-length validation fails.
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 4);
        await using var connection = new StreamConnection(new MemoryStream(bytes));

        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());
    }

    [Fact]
    public async Task ReceiveAsync_Throws_WhenLengthBelowHeaderSize()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, MessageFramer.HeaderSize - 1);
        await using var connection = new StreamConnection(new MemoryStream(bytes));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());
        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
    }

    [Fact]
    public async Task ReceiveAsync_Throws_WhenLengthExceedsMaxMessageSize()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, MessageFramer.MaxMessageSize + 1);
        await using var connection = new StreamConnection(
            new MemoryStream(bytes),
            maxMessageSize: MessageFramer.MaxMessageSize);

        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());
    }

    [Fact]
    public async Task ReceiveAsync_Throws_AfterDispose()
    {
        var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        await connection.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.ReceiveAsync());
    }

    [Fact]
    public async Task ReceiveAsync_PropagatesFault_AndDisposesFrame_WhenBodyReadThrows()
    {
        // Prefix reads cleanly, then the body read faults. StreamConnection must dispose the pooled
        // frame and rethrow the original exception (the catch around the body read).
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, MessageFramer.HeaderSize + 4);
        await using var stream = new FaultAfterPrefixStream(prefix);
        await using var connection = new StreamConnection(stream, ownsStream: false);

        var ex = await Assert.ThrowsAsync<IOException>(() => connection.ReceiveAsync());
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task ReceiveAsync_Throws_WhenCancelled()
    {
        await using var stream = new NeverReturnsStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var cts = new CancellationTokenSource();
        var receiveTask = connection.ReceiveAsync(cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => receiveTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ReceiveAsync_ConcurrentCall_ThrowsInvalidOperation()
    {
        await using var stream = new NeverReturnsStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var cts = new CancellationTokenSource();
        var firstReceive = connection.ReceiveAsync(cts.Token);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.ReceiveAsync().WaitAsync(Timeout));

        Assert.Contains("one pending receive", ex.Message);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => firstReceive.WaitAsync(Timeout));
    }

    [Fact]
    public async Task CloseAsync_DisposesOwnedStream_AndIsIdempotent()
    {
        var stream = new TrackingDisposeStream();
        var connection = new StreamConnection(stream, ownsStream: true);

        await connection.CloseAsync();
        await connection.CloseAsync();

        Assert.Equal(1, stream.DisposeCount);
        Assert.False(connection.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeStream_WhenNotOwned()
    {
        var stream = new TrackingDisposeStream();
        var connection = new StreamConnection(stream, ownsStream: false);

        await connection.DisposeAsync();

        Assert.Equal(0, stream.DisposeCount);
    }

    [Fact]
    public async Task IsConnected_ReflectsDisposedState_OnNonPipeStream()
    {
        // For a non-pipe stream IsConnected reflects only the disposed flag (the PipeStream.IsConnected
        // branch is covered by the named-pipe round-trip tests in the sibling file).
        var connection = new StreamConnection(new MemoryStream(), ownsStream: true);
        Assert.True(connection.IsConnected);

        await connection.DisposeAsync();

        Assert.False(connection.IsConnected);
    }
}
