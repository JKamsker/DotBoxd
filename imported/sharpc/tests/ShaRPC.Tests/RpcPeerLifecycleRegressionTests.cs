using System.Buffers;
using System.Buffers.Binary;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using Shared;
using Xunit;

namespace ShaRPC.Tests;

public sealed class RpcPeerLifecycleRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task Provide_AfterStart_Throws()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var peer = RpcPeer
            .Over(serverConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        Assert.Throws<InvalidOperationException>(() => peer.Provide<IGameService>(new TestGameService()));
    }

    [Fact]
    public async Task Provide_ResolvesImplementationFromConfiguredServiceProvider()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var server = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(5),
                    ServiceProvider = new SingleServiceProvider(new TestGameService()),
                })
            .Provide<IGameService>()
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        Assert.NotNull(await client.GetGameService().GetServerStatusAsync());
    }

    [Fact]
    public void RpcPeerOptions_InvalidBoundedValues_ThrowDuringConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RpcPeerOptions { InboundQueueCapacity = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new RpcPeerOptions { MaxPendingRequests = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { QueueFullMode = (ShaRpcQueueFullMode)42 });
    }

    [Fact]
    public async Task MaxPendingRequests_RejectsAdditionalCallsInsteadOfSpinning()
    {
        await using var connection = new BlackHoleConnection();
        await using var peer = RpcPeer
            .Over(
                connection,
                NewSerializer(),
                new RpcPeerOptions
                {
                    MaxPendingRequests = 1,
                    RequestTimeout = TimeSpan.FromSeconds(30),
                })
            .Start();

        var first = peer.InvokeAsync<int>("Service", "Method");
        await connection.FirstSend.WaitAsync(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<ShaRpcException>(() => peer.InvokeAsync<int>("Service", "Method"));
        _ = first.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    [Fact]
    public async Task ReadLoop_DisposesReceivedFrame_WhenProtocolErrorSendFails()
    {
        var serializer = NewSerializer();
        var frame = CreateMalformedRequestFrame(42);
        await using var connection = new FailingSendConnection(frame, closeAfterSendAttempt: false);
        var readError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var peer = RpcPeer.Over(connection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        peer.ReadError += (_, args) => readError.TrySetResult(args.Error);
        peer.Start();

        Assert.IsType<InvalidOperationException>(await readError.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        AssertDisposed(frame);
    }

    [Fact]
    public async Task DispatchError_Raised_WhenResponseSendFails()
    {
        var serializer = NewSerializer();
        var frame = CreateRequestFrame(serializer, 43, NoopDispatcher.Service, "Call");
        await using var connection = new FailingSendConnection(frame, closeAfterSendAttempt: true);
        var dispatchError = new TaskCompletionSource<RpcDispatchErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = null,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)new NoopDispatcher());
        peer.DispatchError += (_, args) => dispatchError.TrySetResult(args);
        peer.Start();

        var args = await dispatchError.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(43, args.MessageId);
        Assert.Equal(NoopDispatcher.Service, args.ServiceName);
        Assert.Equal("Call", args.MethodName);
        Assert.IsType<InvalidOperationException>(args.Error);
        AssertDisposed(frame);
    }

    private static Payload CreateMalformedRequestFrame(int messageId)
    {
        var body = new byte[MessageFramer.EnvelopeLengthSize + 1];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, MessageFramer.EnvelopeLengthSize), 1);
        body[MessageFramer.EnvelopeLengthSize] = 0xc1;
        return MessageFramer.FrameToPayload(messageId, MessageType.Request, body);
    }

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId, string service, string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = service,
                MethodName = method,
            },
            ReadOnlySpan<byte>.Empty);

    private static void AssertDisposed(Payload frame)
    {
        Assert.Throws<ObjectDisposedException>(() => frame.Memory);
    }

    private sealed class NoopDispatcher : IServiceDispatcher
    {
        public const string Service = "Noop";

        public string ServiceName => Service;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class SingleServiceProvider : IServiceProvider
    {
        private readonly object _service;

        public SingleServiceProvider(object service) => _service = service;

        public object? GetService(Type serviceType) =>
            serviceType.IsInstanceOfType(_service) ? _service : null;
    }

    private sealed class BlackHoleConnection : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _firstSend =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _disposedSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "test://black-hole";

        public Task FirstSend => _firstSend.Task;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            _firstSend.TrySetResult(true);
            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _disposedSignal))
            {
                await _disposedSignal.Task.ConfigureAwait(false);
            }

            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _disposedSignal.TrySetResult(true);
            return default;
        }
    }

    private sealed class FailingSendConnection : IRpcChannel
    {
        private readonly Payload _frame;
        private readonly bool _closeAfterSendAttempt;
        private readonly TaskCompletionSource<bool> _sendAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;
        private int _received;

        public FailingSendConnection(Payload frame, bool closeAfterSendAttempt)
        {
            _frame = frame;
            _closeAfterSendAttempt = closeAfterSendAttempt;
        }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "test://failing-send";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            _sendAttempted.TrySetResult(true);
            throw new InvalidOperationException("send failed");
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _received, 1) == 0)
            {
                return _frame;
            }

            if (_closeAfterSendAttempt)
            {
                await _sendAttempted.Task.WaitAsync(ct).ConfigureAwait(false);
                return Payload.Empty;
            }

            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _sendAttempted.TrySetResult(true);
            return default;
        }
    }
}
