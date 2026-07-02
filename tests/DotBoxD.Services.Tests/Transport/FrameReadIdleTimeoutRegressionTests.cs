using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;
using ServiceFrameReadTimeoutSource = DotBoxD.Services.Protocol.FrameReadTimeoutSource;
using TcpFrameReadTimeoutSource = DotBoxD.Transports.Tcp.FrameReadTimeoutSource;

namespace DotBoxD.Services.Tests.Transport;

public sealed class FrameReadIdleTimeoutRegressionTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task StreamConnection_DefaultFrameReadIdleTimeout_IsFinite()
    {
        await using var connection = new StreamConnection(new MemoryStream());

        Assert.NotEqual(System.Threading.Timeout.InfiniteTimeSpan, connection.FrameReadIdleTimeout);
    }

    [Fact]
    public async Task StreamConnection_ReceiveAsync_AbortsInitialByteStall()
    {
        await using var stream = new PrefixThenStallStream(Array.Empty<byte>());
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: IdleTimeout);

        await Assert.ThrowsAsync<IOException>(() => connection.ReceiveAsync().WaitAsync(Guard));
    }

    [Fact]
    public async Task TcpConnection_ReceiveAsync_AbortsInitialByteStall()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0)
        {
            FrameReadIdleTimeout = IdleTimeout,
        };
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port);
        await using var serverConnection = await acceptTask.WaitAsync(Guard);

        await Assert.ThrowsAsync<IOException>(() => serverConnection.ReceiveAsync().WaitAsync(Guard));
    }

    [Fact]
    public async Task MessageFramer_ReadMessageAsync_AbortsInitialByteStall()
    {
        using var stream = new PrefixThenStallStream(Array.Empty<byte>());

        await Assert.ThrowsAsync<IOException>(
            () => MessageFramer.ReadMessageAsync(stream, IdleTimeout).WaitAsync(Guard));
    }

    [Fact]
    public async Task MessageFramer_ReadMessageAsync_AbortsPayloadStall()
    {
        var header = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), MessageFramer.HeaderSize + 4);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), 7);
        header[8] = (byte)MessageType.Request;
        using var stream = new PrefixThenStallStream(header);

        await Assert.ThrowsAsync<IOException>(
            () => MessageFramer.ReadMessageAsync(stream, IdleTimeout).WaitAsync(Guard));
    }

    [Fact]
    public void ServiceFrameReadTimeoutSource_CancelPendingTimeout_IgnoresDisposedSource()
    {
        using var source = new ServiceFrameReadTimeoutSource();
        source.Start(CancellationToken.None, IdleTimeout);
        DisposeInnerCancellationTokenSource(source);

        var exception = Record.Exception(source.CancelPendingTimeout);

        Assert.Null(exception);
    }

    [Fact]
    public void TcpFrameReadTimeoutSource_CancelPendingTimeout_IgnoresDisposedSource()
    {
        using var source = new TcpFrameReadTimeoutSource();
        source.Start(CancellationToken.None, IdleTimeout);
        DisposeInnerCancellationTokenSource(source);

        var exception = Record.Exception(source.CancelPendingTimeout);

        Assert.Null(exception);
    }

    private static void DisposeInnerCancellationTokenSource(object timeoutSource)
    {
        var field = timeoutSource.GetType().GetField(
            "_source",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var source = Assert.IsType<CancellationTokenSource>(field.GetValue(timeoutSource));
        source.Dispose();
    }

    private sealed class PrefixThenStallStream : Stream
    {
        private readonly byte[] _prefix;
        private int _position;

        public PrefixThenStallStream(byte[] prefix) => _prefix = prefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _prefix.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < _prefix.Length)
            {
                var count = Math.Min(buffer.Length, _prefix.Length - _position);
                _prefix.AsSpan(_position, count).CopyTo(buffer.Span);
                _position += count;
                return count;
            }

            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
