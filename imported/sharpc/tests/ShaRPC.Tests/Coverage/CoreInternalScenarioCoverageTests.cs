using System.Buffers;
using System.Threading.Channels;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov.Round2Internal;

/// <summary>
/// Round-2 behavioral coverage that drives specific internal teardown / error / cancellation paths
/// through the public <see cref="RpcHost"/> and <see cref="RpcPeer"/> surface plus purpose-built
/// fake channels and transports. Every scenario asserts an observable outcome (event raised, exception
/// type/message, peer/host state, clean teardown within a bounded timeout). Pure thread-race and
/// genuinely unreachable defensive branches are deliberately left to the test manifest rather than
/// covered with flaky or sleep-based tests.
/// </summary>
public sealed class CoreInternalScenarioCoverageTests
{
    private const string Service = "Svc";
    private const string Method = "Op";
    private static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Timeout10s = TimeSpan.FromSeconds(10);

    private static MessagePackRpcSerializer NewSerializer() => new();

    // ------------------------------------------------------------------------------------------
    // 1) RpcHostAcceptLoop: an accept that ALWAYS faults, then the host is stopped. Cancellation
    //    lands during the post-error backoff (or during the next AcceptAsync), so DelayAfterErrorAsync
    //    observes the cancelled token and returns false, breaking the loop (lines 41-43, 113-115).
    //    The existing host test only faults-then-accepts, which never cancels during backoff.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task AcceptLoop_AlwaysFaulting_CancelDuringBackoff_ExitsCleanly()
    {
        var transport = new AlwaysFaultServerTransport();
        var firstError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var host = RpcHost.Listen(transport, NewSerializer());
        host.AcceptError += (_, args) => firstError.TrySetResult(args.Error);
        await host.StartAsync().WaitAsync(Timeout5s);

        // Prove the loop entered its fault/backoff cycle at least once before we cancel it.
        var reported = await firstError.Task.WaitAsync(Timeout5s);
        Assert.IsType<InvalidOperationException>(reported);

        // Stopping cancels the loop's token. Because the transport never returns a connection and never
        // throws OperationCanceledException itself, the only way the loop can terminate is the
        // cancel-during-backoff break — so a clean StopAsync within the timeout proves that path ran.
        await host.StopAsync().WaitAsync(Timeout10s);

        // The host is fully torn down and re-disposable without faulting.
        await host.DisposeAsync().AsTask().WaitAsync(Timeout5s);
        Assert.True(transport.WasStopped);
    }

    // ------------------------------------------------------------------------------------------
    // 2) RpcHost.StartAsync dispose-during-start: the listener's StartAsync is held open until the
    //    host has been concurrently disposed. When StartAsync returns, the post-start lock observes
    //    _disposed != 0 and runs the start-after-dispose cleanup (stops the started listener, throws
    //    ObjectDisposedException) instead of launching the accept loop (RpcHost 118-132, 149-172).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_DisposedWhileListenerStarting_ThrowsObjectDisposed_AndStopsListener()
    {
        var transport = new GatedStartServerTransport();
        var host = RpcHost.Listen(transport, NewSerializer());

        var startTask = host.StartAsync();

        // Wait until StartAsync is genuinely parked inside listener.StartAsync (so _cts/_starting are set
        // but the accept loop has not launched), then dispose the host concurrently.
        await transport.StartEntered.Task.WaitAsync(Timeout5s);
        var disposeTask = host.DisposeAsync().AsTask();

        // Let listener.StartAsync complete; StartAsync now sees the disposed host and unwinds.
        transport.ReleaseStart();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => startTask.WaitAsync(Timeout10s));
        await disposeTask.WaitAsync(Timeout10s);

        // Cleanup stopped the listener it had started, and the listener was disposed by DisposeAsync.
        Assert.True(transport.WasStopped);
        Assert.True(transport.WasDisposed);
    }

    // ------------------------------------------------------------------------------------------
    // 3) RpcHostPeerCollection.CloseAllAsync: a peer whose channel dispose THROWS must be swallowed by
    //    DisposeOnePeerAsync's best-effort catch (lines 50-59) so host stop still completes.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task HostStop_PeerDisposeThrows_IsSwallowed_AndStopCompletes()
    {
        // Server side never closes on its own (ReceiveAsync parks) and throws on dispose, so the only
        // disposer is the host's CloseAllAsync during StopAsync.
        var serverChannel = new DisposeThrowingChannel(closeAfterFirstReceive: false);
        var transport = new SingleConnectionServerTransport(serverChannel);
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.Provide((IServiceDispatcher)new NoopDispatcher()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        var peer = await connected.Task.WaitAsync(Timeout5s);
        Assert.True(peer.IsConnected);

        // CloseAllAsync disposes the tracked peer; the peer's channel dispose throws, which
        // DisposeOnePeerAsync swallows so StopAsync still returns cleanly.
        await host.StopAsync().WaitAsync(Timeout10s);
        Assert.True(serverChannel.DisposeWasAttempted);
    }

    // ------------------------------------------------------------------------------------------
    // 3b) RpcHostPeerCollection.DisposeInBackground + AwaitCleanupAsync: a peer that disconnects
    //     naturally is disposed off the read-loop callback; its channel dispose throws, exercising the
    //     background-dispose catch (22-25) and the AwaitCleanupAsync best-effort catch (74-77).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task PeerNaturalDisconnect_BackgroundDisposeThrows_IsSwallowed_AndHostStopsCleanly()
    {
        // The server channel returns one frame attempt then signals close (Payload.Empty), so the
        // accepted peer's read loop ends on its own -> OnPeerDisconnected -> DisposeInBackground.
        var serverChannel = new DisposeThrowingChannel(closeAfterFirstReceive: true);
        var transport = new SingleConnectionServerTransport(serverChannel);
        var disconnected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.Provide((IServiceDispatcher)new NoopDispatcher()));
        host.PeerDisconnected += (_, args) => disconnected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        // The accepted peer disconnects as soon as the read loop sees the close signal.
        var peer = await disconnected.Task.WaitAsync(Timeout10s);
        Assert.NotNull(peer);

        // StopAsync awaits the background cleanup task; the peer dispose faulted, so AwaitCleanupAsync's
        // catch swallows it and stop still completes within the timeout.
        await host.StopAsync().WaitAsync(Timeout10s);
        Assert.True(serverChannel.DisposeWasAttempted);
    }

    // ------------------------------------------------------------------------------------------
    // 4) RpcPeerInboundDispatcher.SendQueueFullErrorAsync: when a request is shed under DropIncoming
    //    backpressure AND the queue-full error frame cannot be sent, the best-effort catch swallows the
    //    send fault (lines 117-125) without tearing down the peer.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task QueueFull_WhenErrorSendFails_SwallowsFault_AndPeerStaysAlive()
    {
        var serializer = NewSerializer();
        await using var connection = new SendFailingScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        // Capacity 1 + a blocking dispatcher: the first request occupies the slot, the overflow request
        // is dropped and the dispatcher tries to send a QueueFull error, which this channel fails.
        connection.Enqueue(CreateRequestFrame(serializer, 1, BlockingDispatcher.Service, "Hold"));
        connection.Enqueue(CreateRequestFrame(serializer, 2, BlockingDispatcher.Service, "Hold"));
        connection.Enqueue(CreateRequestFrame(serializer, 3, BlockingDispatcher.Service, "Hold"));

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    MaxInboundBytes = null,
                    QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
                    RequestTimeout = Timeout5s,
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(Timeout5s);

        // The dispatcher attempted (and failed) to send at least one QueueFull error frame for an
        // overflow request; the failure was swallowed so the peer never tore down.
        await connection.WaitForSendAttemptsAsync(1, Timeout10s);
        Assert.True(peer.IsConnected);

        dispatcher.Release();
    }

    // ------------------------------------------------------------------------------------------
    // 6a) RpcPeerCancelFrameSender.SendAsync: when emitting a cancel frame fails, the exception is
    //     reported and swallowed (lines 90-93) — it never faults the outbound call or the peer.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task CancelFrame_SendThrows_IsSwallowed_AndPeerStaysUsable()
    {
        var serializer = NewSerializer();
        await using var channel = new CancelSendControlChannel(faultCancelSends: true);
        await using var peer = RpcPeer
            .Over(channel, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .Start();

        using var cts = new CancellationTokenSource();
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1, cts.Token);

        // Make sure the request frame is on the wire before cancelling, so the cancel-frame path runs.
        await channel.RequestSent.Task.WaitAsync(Timeout5s);
        cts.Cancel();

        // The call still fails with cancellation even though emitting the cancel frame threw internally.
        await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(Timeout10s));

        // The cancel-frame send was attempted (and threw); the peer remains alive and usable.
        await channel.CancelSendAttempted.Task.WaitAsync(Timeout10s);
        Assert.True(peer.IsConnected);

        // A follow-up call still works (the swallowed fault did not corrupt the peer).
        var followUp = peer.InvokeAsync<int, string>(Service, Method, request: 2);
        channel.EnqueueResponse(serializer, messageId: 2, result: "ok");
        Assert.Equal("ok", await followUp.WaitAsync(Timeout10s));
    }

    // ------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId, string service, string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest { MessageId = messageId, ServiceName = service, MethodName = method },
            ReadOnlySpan<byte>.Empty);

    // ------------------------------------------------------------------------------------------
    // Fake transports / channels / dispatchers
    // ------------------------------------------------------------------------------------------

    /// <summary>Server transport whose AcceptAsync always throws a non-cancellation exception and never
    /// returns a connection, so the host accept loop is forced into its perpetual fault/backoff cycle
    /// until its token is cancelled.</summary>
    private sealed class AlwaysFaultServerTransport : IServerTransport
    {
        private int _stopped;

        public bool WasStopped => Volatile.Read(ref _stopped) != 0;

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("accept always faults");

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _stopped, 1);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Server transport whose StartAsync blocks until released, letting a test dispose the host
    /// while a start is in progress.</summary>
    private sealed class GatedStartServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopped;
        private int _disposed;

        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasStopped => Volatile.Read(ref _stopped) != 0;

        public bool WasDisposed => Volatile.Read(ref _disposed) != 0;

        public void ReleaseStart() => _release.TrySetResult(true);

        public async Task StartAsync(CancellationToken ct = default)
        {
            StartEntered.TrySetResult(true);
            await _release.Task.ConfigureAwait(false);
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _stopped, 1);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _release.TrySetResult(true);
            return default;
        }
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> whose DisposeAsync throws. Optionally signals a channel close after
    /// the first receive so an accepted peer's read loop ends on its own (driving the natural-disconnect
    /// background-dispose path); otherwise ReceiveAsync parks until disposed.
    /// </summary>
    private sealed class DisposeThrowingChannel : IRpcChannel
    {
        private readonly bool _closeAfterFirstReceive;
        private readonly TaskCompletionSource<bool> _parked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _receiveCalls;
        private int _disposeAttempts;

        public DisposeThrowingChannel(bool closeAfterFirstReceive) =>
            _closeAfterFirstReceive = closeAfterFirstReceive;

        public bool IsConnected => Volatile.Read(ref _disposeAttempts) == 0;

        public string RemoteEndpoint => "dispose-throwing://remote";

        public bool DisposeWasAttempted => Volatile.Read(ref _disposeAttempts) != 0;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            if (_closeAfterFirstReceive && Interlocked.Increment(ref _receiveCalls) == 1)
            {
                // Signal an orderly remote close so the read loop ends and the host disconnects the peer.
                return Payload.Empty;
            }

            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _parked))
            {
                await _parked.Task.ConfigureAwait(false);
            }

            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeAttempts);
            _parked.TrySetResult(true);
            throw new InvalidOperationException("channel dispose failed");
        }
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> that delivers enqueued inbound frames but fails every send and counts
    /// the send attempts, so a test can assert the dispatcher tried to emit a (failing) error frame.
    /// </summary>
    private sealed class SendFailingScriptedConnection : IRpcChannel
    {
        private readonly Channel<Payload> _inbound =
            Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource<bool> Completion)> _waiters = new();
        private int _sendAttempts;
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "send-failing-scripted://remote";

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            CompleteWaiters(Interlocked.Increment(ref _sendAttempts));
            return Task.FromException(new InvalidOperationException("send disabled"));
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public Task WaitForSendAttemptsAsync(int count, TimeSpan timeout)
        {
            TaskCompletionSource<bool> completion;
            lock (_gate)
            {
                if (Volatile.Read(ref _sendAttempts) >= count)
                {
                    return Task.CompletedTask;
                }

                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, completion));
            }

            return completion.Task.WaitAsync(timeout);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            _inbound.Writer.TryComplete();
            while (_inbound.Reader.TryRead(out var frame))
            {
                frame.Dispose();
            }

            return default;
        }

        private void CompleteWaiters(int count)
        {
            List<TaskCompletionSource<bool>>? completed = null;
            lock (_gate)
            {
                for (var i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (count < _waiters[i].Count)
                    {
                        continue;
                    }

                    completed ??= new List<TaskCompletionSource<bool>>();
                    completed.Add(_waiters[i].Completion);
                    _waiters.RemoveAt(i);
                }
            }

            if (completed is null)
            {
                return;
            }

            foreach (var completion in completed)
            {
                completion.TrySetResult(true);
            }
        }
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> that distinguishes Request sends from Cancel sends. Request sends
    /// succeed and signal <see cref="RequestSent"/>. Cancel sends throw (to drive the cancel-frame send
    /// fault path) and signal <see cref="CancelSendAttempted"/>. Inbound responses are delivered from a
    /// queue so the peer stays usable after the swallowed cancel-send fault.
    /// </summary>
    private sealed class CancelSendControlChannel : IRpcChannel
    {
        private readonly bool _faultCancelSends;
        private readonly Channel<Payload> _inbound =
            Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });
        private int _disposed;

        public CancelSendControlChannel(bool faultCancelSends) => _faultCancelSends = faultCancelSends;

        public TaskCompletionSource<bool> RequestSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancelSendAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "cancel-control://remote";

        public void EnqueueResponse<TResult>(ISerializer serializer, int messageId, TResult result)
        {
            var response = new RpcResponse { MessageId = messageId, IsSuccess = true };
            var payloadWriter = new ArrayBufferWriter<byte>();
            serializer.Serialize(payloadWriter, result);
            _inbound.Writer.TryWrite(MessageFramer.FrameMessage(
                serializer, messageId, MessageType.Response, response, payloadWriter.WrittenSpan));
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (!MessageFramer.TryReadFrameHeader(data, out _, out var type))
            {
                return Task.CompletedTask;
            }

            if (type == MessageType.Cancel)
            {
                CancelSendAttempted.TrySetResult(true);
                return _faultCancelSends
                    ? Task.FromException(new InvalidOperationException("cancel send disabled"))
                    : Task.CompletedTask;
            }

            if (type == MessageType.Request)
            {
                RequestSent.TrySetResult(true);
            }

            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            _inbound.Writer.TryComplete();
            while (_inbound.Reader.TryRead(out var frame))
            {
                frame.Dispose();
            }

            return default;
        }
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
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Round2Blocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => Service;

        public Task FirstEntered => _firstEntered.Task;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            _firstEntered.TrySetResult(true);
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult(true);
    }
}
