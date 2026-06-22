using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Transport;
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
    public async Task InvokeValueAsync_UsesTaskBackedResponsePath_ByDefault()
    {
        await using var harness = new ValueTaskInvokerHarness(
            new RpcPeerOptions { RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan });
        var call = harness.Invoker.InvokeValueAsync<int, string>("Svc", "Op", 42);
        Assert.Equal(0, harness.SendTaskCalls);
        Assert.Equal(1, harness.SendFrameCalls);
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
    public async Task InvokeAsync_TaskResult_UsesFrameSender_WhenAvailable()
    {
        await using var harness = new ValueTaskInvokerHarness(
            new RpcPeerOptions { RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan });
        var call = harness.Invoker.InvokeAsync<int, string>("Svc", "Op", 42);
        Assert.Equal(0, harness.SendTaskCalls);
        Assert.Equal(1, harness.SendFrameCalls);
        await AssertFaultedPendingCallAsync(call, harness);
    }
    [Fact]
    public async Task InvokeAsync_TaskNoResult_UsesFrameSender_WhenAvailable()
    {
        await using var harness = new ValueTaskInvokerHarness(
            new RpcPeerOptions { RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan });
        var call = harness.Invoker.InvokeAsync<int>("Svc", "Op", 42);
        Assert.Equal(0, harness.SendTaskCalls);
        Assert.Equal(1, harness.SendFrameCalls);
        await AssertFaultedPendingCallAsync(call, harness);
    }
    [Fact]
    public async Task InvokeValueAsync_NoResult_UsesFrameValueTaskPath_WhenExplicitlyEnabled()
    {
        await using var harness = new ValueTaskInvokerHarness(
            new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan,
            });
        var call = harness.Invoker.InvokeValueAsync<int>("Svc", "Op", 42);

        Assert.Equal(0, harness.SendTaskCalls);
        Assert.Equal(1, harness.SendFrameCalls);
        Assert.True(harness.Invoker.TryCompleteResponse(1, MessageFramer.FrameMessage(
            new MessagePackRpcSerializer(),
            1,
            MessageType.Response,
            new RpcResponse { MessageId = 1, IsSuccess = true },
            ReadOnlySpan<byte>.Empty)));
        await call.AsTask().WaitAsync(Timeout);
    }

    [Fact]
    public async Task InvokeValueAsync_NoResult_FaultsPendingOnce_OnLowAllocationPath()
    {
        await using var harness = new ValueTaskInvokerHarness(
            new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan,
            });

        var call = harness.Invoker.InvokeValueAsync<int>("Svc", "Op", 42);

        Assert.Equal(0, harness.SendTaskCalls);
        Assert.Equal(1, harness.SendFrameCalls);

        // The pooled no-response IValueTaskSource (PendingValueTaskNoResponse) must surface the connection
        // failure, and a redundant failure must not re-complete or throw on the already-faulted source.
        harness.Invoker.FailPending(new ServiceConnectionException("Connection closed."));
        harness.Invoker.FailPending(new ServiceConnectionException("Connection closed again."));

        await Assert.ThrowsAsync<ServiceConnectionException>(() => call.AsTask().WaitAsync(Timeout));
    }

    [Fact]
    public async Task InvokeValueAsync_OptInUsesTaskBackedResponsePath_WhenTimeoutOrCancellationIsRequired()
    {
        await using (var timeoutHarness = new ValueTaskInvokerHarness(
            new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                RequestTimeout = TimeSpan.FromSeconds(1),
            }))
        {
            var call = timeoutHarness.Invoker.InvokeValueAsync<int, string>("Svc", "Op", 42);

            Assert.Equal(0, timeoutHarness.SendTaskCalls);
            Assert.Equal(1, timeoutHarness.SendFrameCalls);
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

        Assert.Equal(0, cancellationHarness.SendTaskCalls);
        Assert.Equal(1, cancellationHarness.SendFrameCalls);
        await AssertFaultedPendingCallAsync(cancellableCall, cancellationHarness);
    }

    private static async Task AssertFaultedPendingCallAsync<T>(
        ValueTask<T> call,
        ValueTaskInvokerHarness harness)
    {
        harness.Invoker.FailPending(new ServiceConnectionException("Connection closed."));
        await Assert.ThrowsAsync<ServiceConnectionException>(
            () => call.AsTask().WaitAsync(Timeout));
    }

    private static async Task AssertFaultedPendingCallAsync<T>(
        Task<T> call,
        ValueTaskInvokerHarness harness)
    {
        harness.Invoker.FailPending(new ServiceConnectionException("Connection closed."));
        await Assert.ThrowsAsync<ServiceConnectionException>(() => call.WaitAsync(Timeout));
    }

    private static async Task AssertFaultedPendingCallAsync(
        Task call,
        ValueTaskInvokerHarness harness)
    {
        harness.Invoker.FailPending(new ServiceConnectionException("Connection closed."));
        await Assert.ThrowsAsync<ServiceConnectionException>(
            () => call.WaitAsync(Timeout));
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

            Invoker.FailPending(new ServiceConnectionException("Connection closed."));
            await Invoker.StopCancelFramesAsync().ConfigureAwait(false);
            _streams.Stop();
        }
    }
}
