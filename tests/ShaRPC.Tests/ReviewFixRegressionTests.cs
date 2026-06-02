using System.Buffers;
using System.Threading;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression tests for the review findings fixed on this branch: the outbound pending-slot leak
/// (H1), the legacy server start/dispose race (H3), the client connect/dispose race (L12), and
/// <see cref="RpcPeerOptions.RequestTimeout"/> validation (M6).
/// </summary>
public sealed class ReviewFixRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    // H1: a synchronous serialization failure must release the reserved pending-request slot,
    // otherwise the bounded admission gate leaks a slot per failure and eventually rejects every
    // call with "Maximum pending requests reached" even though nothing is in flight.
    [Fact]
    public async Task OutboundSerializationFailure_DoesNotLeakPendingSlot()
    {
        var serializer = new PoisonSerializer(NewSerializer());
        await using var connection = new BlackHoleConnection();
        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    MaxPendingRequests = 1,
                    RequestTimeout = TimeSpan.FromMilliseconds(250),
                })
            .Start();

        // Each call fails while serializing its argument. With the slot leaked, the SECOND such
        // call would already throw ShaRpcException("Maximum pending requests reached.") instead of
        // the serialization error.
        for (var i = 0; i < 5; i++)
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(
                () => peer.InvokeAsync<PoisonArgument, int>("Service", "Method", new PoisonArgument()));
            Assert.IsNotType<ShaRpcException>(failure);
        }

        // A subsequent well-formed call still gets a slot and reaches the wire, timing out because
        // the black-hole connection never answers — proving the slots were reclaimed.
        await Assert.ThrowsAsync<ShaRpcTimeoutException>(
            () => peer.InvokeAsync<int>("Service", "Method").WaitAsync(TimeSpan.FromSeconds(2)));
    }

    // H3: disposing the legacy server while StartAsync is still awaiting the transport must not
    // NRE on a nulled _cts nor launch an accept loop on an already-stopped transport.
    [Fact]
    public async Task ShaRpcServer_DisposeDuringStart_DoesNotStartAcceptLoopAfterDispose()
    {
        var transport = new DelayedStartServerTransport();
        var server = new ShaRpcServer(transport, NewSerializer());

        var startTask = server.StartAsync();
        await transport.StartEntered.WaitAsync(TimeSpan.FromSeconds(1));

        await server.DisposeAsync();
        transport.AllowStart();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => startTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, transport.AcceptCalls);
        Assert.True(transport.StopCalls > 0);
    }

    // L12: disposing the client while ConnectAsync is still awaiting the transport must tear down
    // the receive loop it started rather than leaving it (and a CTS) running on a disposed transport.
    [Fact]
    public async Task ShaRpcClient_DisposeDuringConnect_FailsConnectAndStartsNoLingeringLoop()
    {
        var transport = new DelayedConnectTransport();
        var client = new ShaRpcClient(transport, NewSerializer());

        var connectTask = client.ConnectAsync();
        await transport.ConnectEntered.WaitAsync(TimeSpan.FromSeconds(1));

        await client.DisposeAsync();
        transport.AllowConnect();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connectTask.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    // M6: RequestTimeout is validated at construction like the other tunables.
    [Fact]
    public void RpcPeerOptions_InvalidRequestTimeout_ThrowsDuringConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RpcPeerOptions { RequestTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(-5) });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { RequestTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1) });

        // Positive timeouts and the infinite sentinel are accepted.
        _ = new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(1) };
        _ = new RpcPeerOptions { RequestTimeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class PoisonArgument
    {
    }

    /// <summary>Serializer that throws when asked to serialize a <see cref="PoisonArgument"/>.</summary>
    private sealed class PoisonSerializer : ISerializer
    {
        private readonly ISerializer _inner;

        public PoisonSerializer(ISerializer inner) => _inner = inner;

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (typeof(T) == typeof(PoisonArgument))
            {
                throw new InvalidOperationException("Cannot serialize the poison argument.");
            }

            _inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => _inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => _inner.Deserialize(data, type);
    }

    /// <summary>Accepts every send and never produces an inbound frame until disposed.</summary>
    private sealed class BlackHoleConnection : IConnection
    {
        private readonly TaskCompletionSource<bool> _disposedSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "test://black-hole";

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

        public Task<IConnection> AcceptAsync(CancellationToken ct = default)
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

    private sealed class DelayedConnectTransport : ITransport
    {
        private readonly TaskCompletionSource<bool> _connectEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowConnect =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly IdleConnection _connection = new();
        private int _connected;
        private int _disposed;

        public Task ConnectEntered => _connectEntered.Task;

        public IConnection? Connection => Volatile.Read(ref _connected) != 0 ? _connection : null;

        public bool IsConnected => Volatile.Read(ref _connected) != 0 && Volatile.Read(ref _disposed) == 0;

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            _connectEntered.TrySetResult(true);
            await _allowConnect.Task.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Exchange(ref _connected, 1);
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        public void AllowConnect() => _allowConnect.TrySetResult(true);

        private sealed class IdleConnection : IConnection
        {
            private int _disposed;

            public bool IsConnected => Volatile.Read(ref _disposed) == 0;

            public string RemoteEndpoint => "test://idle";

            public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

            // The receive loop only starts after a concurrent dispose, so return the closed signal
            // immediately to let it exit promptly during teardown.
            public Task<Payload> ReceiveAsync(CancellationToken ct = default) => Task.FromResult(Payload.Empty);

            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _disposed, 1);
                return default;
            }
        }
    }
}
