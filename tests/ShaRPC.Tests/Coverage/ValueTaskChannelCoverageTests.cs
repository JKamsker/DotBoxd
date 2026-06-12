using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov.Transport;

public sealed class ValueTaskChannelCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RpcPeer_UsesValueTaskChannelMethods_WhenAvailable()
    {
        await using var channel = new CountingValueTaskChannel();
        await using var peer = RpcPeer
            .Over(
                channel,
                new MessagePackRpcSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) })
            .Start();

        await channel.ReceiveCalled.Task.WaitAsync(Timeout);

        var call = peer.InvokeAsync<int>("Svc", "Op");
        await channel.SendCalled.Task.WaitAsync(Timeout);

        Assert.Equal(1, channel.SendValueCalls);
        Assert.Equal(0, channel.SendTaskCalls);
        Assert.Equal(1, channel.ReceiveValueCalls);
        Assert.Equal(0, channel.ReceiveTaskCalls);

        await peer.DisposeAsync();
        var completed = await Task.WhenAny(call, Task.Delay(Timeout));
        Assert.Same(call, completed);
        await Assert.ThrowsAnyAsync<Exception>(async () => await call);
    }

    [Fact]
    public async Task InvokeValueAsync_UsesTaskBackedPath_ByDefault()
    {
        await using var harness = new ValueTaskInvokerHarness(
            new RpcPeerOptions { RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan });

        var call = harness.Invoker.InvokeValueAsync<int, string>("Svc", "Op", 42);

        Assert.Equal(1, harness.SendTaskCalls);
        Assert.Equal(0, harness.SendFrameCalls);
        await AssertFaultedPendingCallAsync(call, harness);
    }

    [Fact]
    public async Task InvokeValueAsync_UsesFrameValueTaskPath_WhenExplicitlyEnabled()
    {
        await using var harness = new ValueTaskInvokerHarness(
            new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan,
            });

        var call = harness.Invoker.InvokeValueAsync<int, string>("Svc", "Op", 42);

        Assert.Equal(0, harness.SendTaskCalls);
        Assert.Equal(1, harness.SendFrameCalls);
        await AssertFaultedPendingCallAsync(call, harness);
    }

    [Fact]
    public async Task InvokeValueAsync_OptInUsesTaskBackedPath_WhenTimeoutOrCancellationIsRequired()
    {
        await using (var timeoutHarness = new ValueTaskInvokerHarness(
            new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                RequestTimeout = TimeSpan.FromSeconds(1),
            }))
        {
            var call = timeoutHarness.Invoker.InvokeValueAsync<int, string>("Svc", "Op", 42);

            Assert.Equal(1, timeoutHarness.SendTaskCalls);
            Assert.Equal(0, timeoutHarness.SendFrameCalls);
            await AssertFaultedPendingCallAsync(call, timeoutHarness);
        }

        using var cts = new CancellationTokenSource();
        await using var cancellationHarness = new ValueTaskInvokerHarness(
            new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan,
            });

        var cancellableCall = cancellationHarness.Invoker.InvokeValueAsync<int, string>(
            "Svc",
            "Op",
            42,
            cts.Token);

        Assert.Equal(1, cancellationHarness.SendTaskCalls);
        Assert.Equal(0, cancellationHarness.SendFrameCalls);
        await AssertFaultedPendingCallAsync(cancellableCall, cancellationHarness);
    }

    private static async Task AssertFaultedPendingCallAsync<T>(
        ValueTask<T> call,
        ValueTaskInvokerHarness harness)
    {
        harness.Invoker.FailPending(new ShaRpcConnectionException("Connection closed."));
        await Assert.ThrowsAsync<ShaRpcConnectionException>(
            () => call.AsTask().WaitAsync(Timeout));
    }

    private sealed class CountingValueTaskChannel : IRpcValueTaskChannel
    {
        private readonly TaskCompletionSource<Payload> _receive =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => true;

        public string RemoteEndpoint => "valuetask://test";

        public int SendTaskCalls { get; private set; }

        public int SendValueCalls { get; private set; }

        public int ReceiveTaskCalls { get; private set; }

        public int ReceiveValueCalls { get; private set; }

        public TaskCompletionSource SendCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReceiveCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            SendTaskCalls++;
            return SendValueAsync(data, ct).AsTask();
        }

        public ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            SendValueCalls++;
            SendCalled.TrySetResult();
            return default;
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            ReceiveTaskCalls++;
            return ReceiveValueAsync(ct).AsTask();
        }

        public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default)
        {
            ReceiveValueCalls++;
            ReceiveCalled.TrySetResult();
            return new ValueTask<Payload>(_receive.Task);
        }

        public ValueTask DisposeAsync()
        {
            _receive.TrySetResult(Payload.Empty);
            return default;
        }
    }

    private sealed class ValueTaskInvokerHarness : IAsyncDisposable
    {
        private readonly MessagePackRpcSerializer _serializer = new();
        private readonly RpcStreamManager _streams;
        private int _disposed;

        public ValueTaskInvokerHarness(RpcPeerOptions options)
        {
            _streams = new RpcStreamManager(_serializer, SendAsync, exceptionTransformer: null);
            Invoker = new RpcPeerOutboundInvoker(
                _serializer,
                options,
                ensureStarted: static () => { },
                SendAsync,
                SendFrameValueAsync,
                _streams);
        }

        public RpcPeerOutboundInvoker Invoker { get; }

        public int SendTaskCalls { get; private set; }

        public int SendFrameCalls { get; private set; }

        private Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            SendTaskCalls++;
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default)
        {
            SendFrameCalls++;
            try
            {
                ct.ThrowIfCancellationRequested();
                return default;
            }
            finally
            {
                frame.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Invoker.FailPending(new ShaRpcConnectionException("Connection closed."));
            await Invoker.StopCancelFramesAsync().ConfigureAwait(false);
            _streams.Stop();
        }
    }
}
