using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostLifecycleRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    // H2: a connection accepted just before shutdown must not leak. The hand-off is held open until
    // shutdown is already underway; the host must dispose the peer (and its channel) instead of
    // starting a read loop the host would never tear down.
    [Fact]
    public async Task AcceptDuringShutdown_DisposesPeerInsteadOfLeakingIt()
    {
        var connection = new TrackingConnection();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var configureEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerConnectedFired = 0;

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(connection), NewSerializer())
            .ForEachPeer(_ =>
            {
                // Hold the accepted peer's hand-off open until the test has begun shutdown.
                configureEntered.TrySetResult(true);
                gate.Task.GetAwaiter().GetResult();
            });
        host.PeerConnected += (_, _) => Interlocked.Increment(ref peerConnectedFired);
        await host.StartAsync();

        await configureEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Begin shutdown while the hand-off is mid-flight, then let it proceed.
        var stop = host.StopAsync();
        gate.SetResult(true);
        await stop.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(connection.IsConnectionDisposed);
        Assert.Equal(0, Volatile.Read(ref peerConnectedFired));
    }

    [Fact]
    public async Task DisposeDuringStart_DoesNotStartAcceptLoopAfterDispose()
    {
        var transport = new DelayedStartServerTransport();
        await using var host = RpcHost.Listen(transport, NewSerializer());

        var startTask = host.StartAsync();
        await transport.StartEntered.WaitAsync(TimeSpan.FromSeconds(1));

        await host.DisposeAsync();
        transport.AllowStart();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => startTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, transport.AcceptCalls);
        Assert.True(transport.StopCalls > 0);
    }

    [Fact]
    public async Task StopAsync_AfterCanceledTransportStop_CanRetryAndClosePeers()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var transport = new CancelFirstStopServerTransport(serverConnection);
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost.Listen(transport, NewSerializer());
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync();

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();
        var acceptedPeer = await connected.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using var stopCts = new CancellationTokenSource();
        stopCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.StopAsync(stopCts.Token));
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, transport.StopCalls);
        Assert.False(acceptedPeer.IsConnected);
    }

    [Fact]
    public async Task StopAsync_WithPreCanceledToken_StopsListenerBeforeCancelingAcceptLoop()
    {
        var transport = new PreCanceledStopServerTransport();
        var host = RpcHost.Listen(transport, NewSerializer());
        try
        {
            await host.StartAsync();
            await transport.AcceptStarted.WaitAsync(TimeSpan.FromSeconds(1));

            using var stopCts = new CancellationTokenSource();
            stopCts.Cancel();

            await host.StopAsync(stopCts.Token).WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(1, transport.StopCompletedCalls);
            Assert.False(transport.AcceptTokenCanceledWhenStopRan);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task AcceptedPeerConfigurationFailure_RaisesAcceptError()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var failure = new InvalidOperationException("configuration failed");
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(_ => throw failure);
        host.AcceptError += (_, args) => error.TrySetResult(args.Error);
        await host.StartAsync();

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        Assert.Same(failure, await error.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    private sealed class TrackingConnection : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _disposedSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public bool IsConnectionDisposed => Volatile.Read(ref _disposed) != 0;

        public string RemoteEndpoint => "test://tracking";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

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

    private sealed class DelayedStartServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowStart =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _acceptCalls;
        private int _stopCalls;

        public Task StartEntered => _startEntered.Task;

        public int AcceptCalls => Volatile.Read(ref _acceptCalls);

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public async Task StartAsync(CancellationToken ct = default)
        {
            _startEntered.TrySetResult(true);
            await _allowStart.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _acceptCalls);
            throw new InvalidOperationException("The accept loop should not start.");
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;

        public void AllowStart() => _allowStart.TrySetResult(true);
    }

    private sealed class PreCanceledStopServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _acceptStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _acceptTokenCanceled;
        private int _acceptTokenCanceledWhenStopRan;
        private int _stopCompletedCalls;

        public Task AcceptStarted => _acceptStarted.Task;

        public bool AcceptTokenCanceled => Volatile.Read(ref _acceptTokenCanceled) != 0;

        public bool AcceptTokenCanceledWhenStopRan => Volatile.Read(ref _acceptTokenCanceledWhenStopRan) != 0;

        public int StopCompletedCalls => Volatile.Read(ref _stopCompletedCalls);

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            using var registration = ct.Register(
                static state => ((PreCanceledStopServerTransport)state!).MarkAcceptTokenCanceled(),
                this);
            _acceptStarted.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (AcceptTokenCanceled)
            {
                Volatile.Write(ref _acceptTokenCanceledWhenStopRan, 1);
            }

            Interlocked.Increment(ref _stopCompletedCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;

        private void MarkAcceptTokenCanceled() => Volatile.Write(ref _acceptTokenCanceled, 1);
    }

    private sealed class CancelFirstStopServerTransport : IServerTransport
    {
        private readonly IRpcChannel _connection;
        private int _accepted;
        private int _stopCalls;

        public CancelFirstStopServerTransport(IRpcChannel connection) => _connection = connection;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
            {
                return _connection;
            }

            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _stopCalls);
            return call == 1
                ? Task.FromException(new OperationCanceledException())
                : Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }
}
