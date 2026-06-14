using System.Buffers;
using System.Collections.Concurrent;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov.PeerOutbound;

/// <summary>
/// Behavioral coverage for the outbound (client) half of <see cref="RpcPeer"/>: request framing,
/// response correlation, remote-error surfacing, timeouts, cancellation (cancel frames), send
/// failures, and the disposal path that faults pending requests. These drive the internal
/// <c>RpcPeerOutboundInvoker</c>, <c>ShaRpcPendingRequests</c>, <c>ReceivedResponse</c>,
/// <c>RpcPeerSender</c>, and <c>RpcPeerCancelFrameSender</c> purely through the public
/// <see cref="RpcPeer"/> API plus frame injection over <see cref="IRpcChannel"/>.
/// </summary>
public sealed class PeerOutboundCoverageTests
{
    private const string Service = "Svc";
    private const string Method = "Op";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static MessagePackRpcSerializer NewSerializer() => new();

    /// <summary>Polls <paramref name="condition"/> with a bounded deadline so a regression fails fast
    /// instead of hanging, without a fixed sleep used for synchronization.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not satisfied within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private static RpcPeerOptions Options(TimeSpan? requestTimeout = null) =>
        new() { RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Frames a Response (or Error) frame the read loop will correlate to an outbound request by id.
    /// </summary>
    private static Payload ResponseFrame<TResult>(
        ISerializer serializer,
        int messageId,
        TResult result,
        bool isSuccess = true,
        MessageType type = MessageType.Response)
    {
        var response = new RpcResponse
        {
            MessageId = messageId,
            IsSuccess = isSuccess,
        };

        var payloadWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(payloadWriter, result);
        return MessageFramer.FrameMessage(serializer, messageId, type, response, payloadWriter.WrittenSpan);
    }

    private static Payload ErrorFrame(
        ISerializer serializer,
        int messageId,
        string errorMessage,
        string errorType)
    {
        var response = new RpcResponse
        {
            MessageId = messageId,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType,
        };

        return MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Error,
            response,
            ReadOnlySpan<byte>.Empty);
    }

    [Fact]
    public async Task InvokeAsync_WithMatchingResponseFrame_ReturnsDeserializedResult()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // First outbound call on a fresh peer always uses message id 1 (counter is Interlocked.Increment from 0).
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 7);
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "pong"));

        var result = await call.WaitAsync(Timeout);

        Assert.Equal("pong", result);
    }

    [Fact]
    public async Task InvokeAsync_NoRequestNoResponseBody_CompletesWhenResponseFrameArrives()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // Parameterless, void-returning overload: exercises the no-argument SendRequestAsync +
        // FrameMessage(empty) path and the discard-result Invoke overload.
        var call = peer.InvokeAsync(Service, Method);
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer,
            messageId: 1,
            MessageType.Response,
            new RpcResponse { MessageId = 1, IsSuccess = true },
            ReadOnlySpan<byte>.Empty));

        await call.WaitAsync(Timeout);
    }

    [Fact]
    public async Task InvokeAsync_RequestNoResponseBody_CompletesWhenResponseFrameArrives()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // Request payload but void return: exercises the with-argument FrameRequest path and the
        // discard-result Invoke overload (using var _ = ...).
        var call = peer.InvokeAsync<string>(Service, Method, request: "hello");
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer,
            messageId: 1,
            MessageType.Response,
            new RpcResponse { MessageId = 1, IsSuccess = true },
            ReadOnlySpan<byte>.Empty));

        await call.WaitAsync(Timeout);
    }

    [Fact]
    public async Task InvokeAsync_NoRequestWithResponseBody_ReturnsDeserializedResult()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<string>(Service, Method);
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "value"));

        Assert.Equal("value", await call.WaitAsync(Timeout));
    }

    [Fact]
    public async Task InvokeOnInstanceAsync_RoutesThroughInstanceOverloads()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // Each of the four instance overloads in turn; ids increment 1..4 deterministically.
        var withReqResult = peer.InvokeOnInstanceAsync<int, string>(Service, "inst", Method, request: 1);
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "a"));
        Assert.Equal("a", await withReqResult.WaitAsync(Timeout));

        var resultOnly = peer.InvokeOnInstanceAsync<string>(Service, "inst", Method);
        channel.Enqueue(ResponseFrame(serializer, messageId: 2, result: "b"));
        Assert.Equal("b", await resultOnly.WaitAsync(Timeout));

        var reqVoid = peer.InvokeOnInstanceAsync<string>(Service, "inst", Method, request: "x");
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer, 3, MessageType.Response, new RpcResponse { MessageId = 3, IsSuccess = true }, ReadOnlySpan<byte>.Empty));
        await reqVoid.WaitAsync(Timeout);

        var voidOnly = peer.InvokeOnInstanceAsync(Service, "inst", Method);
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer, 4, MessageType.Response, new RpcResponse { MessageId = 4, IsSuccess = true }, ReadOnlySpan<byte>.Empty));
        await voidOnly.WaitAsync(Timeout);
    }

    [Fact]
    public async Task InvokeAsync_MultipleConcurrentRequests_CorrelatedByMessageId()
    {
        var serializer = NewSerializer();
        await using var channel = new RecordingChannel();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        const int callCount = 8;
        var calls = Enumerable.Range(0, callCount)
            .Select(i => peer.InvokeAsync<int, int>(Service, Method, request: i))
            .ToArray();

        // Wait until the peer has actually sent every request frame so we know all ids.
        var ids = await channel.WaitForSentFrameIdsAsync(callCount, Timeout);

        // Respond out of order to prove correlation is by id, not arrival order: each id i (1-based)
        // gets result id*100.
        foreach (var id in Enumerable.Reverse(ids))
        {
            channel.Enqueue(ResponseFrame(serializer, id, result: id * 100));
        }

        var results = await Task.WhenAll(calls).WaitAsync(Timeout);

        // Each call receives the result keyed to ITS OWN message id (id*100). Because the concurrent
        // sends may reserve ids in any order relative to the calls[] array, assert on the multiset:
        // every id's response was delivered to exactly one awaiting call, and none crossed wires.
        var expected = ids.Select(id => id * 100).OrderBy(v => v).ToArray();
        var actual = results.OrderBy(v => v).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(callCount, actual.Distinct().Count());
    }

    [Fact]
    public async Task InvokeAsync_ErrorResponse_ThrowsShaRpcRemoteException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        channel.Enqueue(ErrorFrame(serializer, messageId: 1, "boom", "MyError"));

        var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(() => call.WaitAsync(Timeout));
        Assert.Equal("boom", ex.Message);
        Assert.Equal("MyError", ex.RemoteExceptionType);
    }

    [Fact]
    public async Task InvokeAsync_UnsuccessfulResponseWithNullErrorFields_ThrowsWithDefaults()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        // IsSuccess=false but no ErrorMessage/ErrorType -> the invoker fills in the "Unknown" defaults.
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer,
            messageId: 1,
            MessageType.Response,
            new RpcResponse { MessageId = 1, IsSuccess = false },
            ReadOnlySpan<byte>.Empty));

        var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(() => call.WaitAsync(Timeout));
        Assert.Equal("Unknown error", ex.Message);
        Assert.Equal("Unknown", ex.RemoteExceptionType);
    }

    [Fact]
    public async Task InvokeAsync_MalformedResponseEnvelope_FaultsRequestWithProtocolException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);

        // A well-formed frame header (so the read loop routes it to the outbound invoker) but the
        // envelope bytes are not a valid RpcResponse -> Deserialize throws -> TryFail("Malformed
        // response envelope.").
        var garbageEnvelope = new byte[] { 0xC1 }; // 0xC1 is the MessagePack "never used" byte -> throws.
        var frame = MessageFramerTestExtensions.FrameToPayloadWithGarbageEnvelope(messageId: 1, garbageEnvelope);
        channel.Enqueue(frame);

        var ex = await Assert.ThrowsAsync<ShaRpcProtocolException>(() => call.WaitAsync(Timeout));
        Assert.Contains("Malformed response envelope", ex.Message);
    }

    [Fact]
    public async Task InvokeAsync_ResponseForUnknownMessageId_IsIgnored_RequestStillTimesOut()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(
            channel,
            serializer,
            Options(requestTimeout: TimeSpan.FromMilliseconds(300))).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);

        // Response addressed to an id that was never reserved: TryComplete returns false, the read loop
        // disposes the frame, and the real request (id 1) is left to time out.
        channel.Enqueue(ResponseFrame(serializer, messageId: 999, result: "stray"));

        var ex = await Assert.ThrowsAsync<ShaRpcTimeoutException>(() => call.WaitAsync(Timeout));
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task InvokeAsync_DuplicateResponseForSameId_SecondIsIgnored()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "first"));
        Assert.Equal("first", await call.WaitAsync(Timeout));

        // A second response for the now-removed id must be a harmless no-op (TryComplete -> false),
        // not corrupt later calls. Issue another distinct call to confirm the peer still works.
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "duplicate-ignored"));

        var second = peer.InvokeAsync<int, string>(Service, Method, request: 2);
        channel.Enqueue(ResponseFrame(serializer, messageId: 2, result: "second"));
        Assert.Equal("second", await second.WaitAsync(Timeout));
    }

    [Fact]
    public async Task InvokeAsync_TimesOut_WhenNoResponseArrives()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(
            channel,
            serializer,
            Options(requestTimeout: TimeSpan.FromMilliseconds(200))).Start();

        var ex = await Assert.ThrowsAsync<ShaRpcTimeoutException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 1).WaitAsync(Timeout));
        Assert.Contains($"{Service}.{Method}", ex.Message);
    }

    [Fact]
    public async Task InvokeAsync_TokenCancelled_FaultsRequest_AndSendsCancelFrame()
    {
        var serializer = NewSerializer();
        await using var channel = new RecordingChannel();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        using var cts = new CancellationTokenSource();
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1, cts.Token);

        // Make sure the request frame was actually sent (requestSent == true) before cancelling, so
        // the cancel-frame path is taken.
        await channel.WaitForSentFrameIdsAsync(1, Timeout);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(Timeout));

        // A Cancel frame for the in-flight id must be emitted by RpcPeerCancelFrameSender.
        var cancel = await channel.WaitForFrameOfTypeAsync(MessageType.Cancel, Timeout);
        Assert.Equal(MessageType.Cancel, cancel.Type);
        Assert.Equal(1, cancel.MessageId);
    }

    [Fact]
    public async Task InvokeAsync_PreCancelledToken_ThrowsWithoutReservingASlot()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The reservation loop checks the token before reserving a message id, so an already-cancelled
        // token throws OperationCanceledException and decrements the pending counter (no leak). It also
        // throws before the message-id counter is incremented, so the next call reuses id 1.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 1, cts.Token).WaitAsync(Timeout));

        // The peer must still accept a fresh call afterward (slot was released). The first id never got
        // consumed, so this call is assigned message id 1.
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 2);
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "ok"));
        Assert.Equal("ok", await call.WaitAsync(Timeout));
    }

    [Fact]
    public async Task InvokeAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var serializer = NewSerializer();
        var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, serializer, Options());
        await peer.DisposeAsync();

        // EnsureStarted (called inside SendRequestAsync) observes _disposed and throws.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 1).WaitAsync(Timeout));

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_WithPendingRequest_FaultsItWithConnectionClosed()
    {
        var serializer = NewSerializer();
        var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, serializer, Options());
        peer.Start();

        // In flight, no response queued.
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);

        await peer.DisposeAsync();

        // DisposeCoreAsync -> FailPending(ShaRpcConnectionException("Connection closed.")).
        var ex = await Assert.ThrowsAsync<ShaRpcConnectionException>(() => call.WaitAsync(Timeout));
        Assert.Contains("closed", ex.Message);

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task CloseAsync_WithPendingRequest_FaultsItWithConnectionClosed()
    {
        var serializer = NewSerializer();
        var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, serializer, Options());
        peer.Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        await peer.CloseAsync();

        await Assert.ThrowsAsync<ShaRpcConnectionException>(() => call.WaitAsync(Timeout));

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task CloseAsync_WithCancelledToken_ThrowsBeforeTeardown()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, serializer, Options());
        peer.Start();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => peer.CloseAsync(cts.Token));

        // The peer was not disposed by that throw, so it can still be disposed normally.
        await peer.DisposeAsync();
    }

    [Fact]
    public async Task InvokeAsync_WhenSendFails_SurfacesSendException()
    {
        var serializer = NewSerializer();
        await using var channel = new ThrowingSendChannel(new ShaRpcConnectionException("send-broke"));
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // _sender.SendAsync -> _channel.SendAsync throws; SendFrameAndAwaitAsync never sets requestSent,
        // and the exception propagates out of InvokeAsync after releasing the reserved slot.
        var ex = await Assert.ThrowsAsync<ShaRpcConnectionException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 1).WaitAsync(Timeout));
        Assert.Equal("send-broke", ex.Message);
    }

    [Fact]
    public async Task InvokeAsync_AfterSendFailure_SlotIsReleasedAndPeerStillUsable()
    {
        var serializer = NewSerializer();
        await using var channel = new ToggleSendChannel();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        channel.FailNextSends = true;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 1).WaitAsync(Timeout));

        // After the failed send the admission slot must have been released; a follow-up call works.
        channel.FailNextSends = false;
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 2);
        channel.Enqueue(ResponseFrame(serializer, messageId: 2, result: "recovered"));
        Assert.Equal("recovered", await call.WaitAsync(Timeout));
    }

    [Fact]
    public async Task InvokeAsync_AfterRemoteClose_FaultsWithConnectionClosed_AndSenderRejectsLateSend()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // Park one request so the remote-close teardown has a pending request to fault.
        var inFlight = peer.InvokeAsync<int, string>(Service, Method, request: 1);

        // An empty frame signals the channel closed; the read loop runs StopAfterRemoteCloseAsync,
        // which marks the peer closed and FailPending(ShaRpcConnectionException("Connection closed.")).
        channel.Enqueue(Payload.Empty);

        var ex = await Assert.ThrowsAsync<ShaRpcConnectionException>(() => inFlight.WaitAsync(Timeout));
        Assert.Contains("closed", ex.Message);

        // Once closed, the IsConnected projection flips and any new send fast-fails through the sender's
        // closed-guard rather than parking. Poll briefly for the closed state to settle (set on the read
        // loop) without a fixed sleep.
        await WaitUntilAsync(() => !peer.IsConnected, Timeout);

        await Assert.ThrowsAsync<ShaRpcConnectionException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 2).WaitAsync(Timeout));
    }

    [Theory]
    [InlineData("", "method")]
    [InlineData("service", "")]
    public async Task InvokeAsync_WithEmptyServiceOrMethod_ThrowsArgumentException(string service, string method)
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        await Assert.ThrowsAsync<ArgumentException>(
            () => peer.InvokeAsync<int, string>(service, method, request: 1).WaitAsync(Timeout));
    }

    [Fact]
    public async Task InvokeAsync_ExceedingMaxPendingRequests_ThrowsShaRpcException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(
            channel,
            serializer,
            new RpcPeerOptions
            {
                MaxPendingRequests = 1,
                RequestTimeout = TimeSpan.FromSeconds(30),
            }).Start();

        // First call occupies the single slot and parks awaiting a response.
        var first = peer.InvokeAsync<int, string>(Service, Method, request: 1);

        // Second call cannot reserve a slot (pendingCount would exceed 1) -> ShaRpcException.
        var ex = await Assert.ThrowsAsync<ShaRpcException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 2).WaitAsync(Timeout));
        Assert.Contains("Maximum pending requests", ex.Message);

        // Complete the first so disposal does not have to fault it.
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "done"));
        Assert.Equal("done", await first.WaitAsync(Timeout));
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> that records every framed message it is asked to send (so tests can
    /// learn the message ids the peer assigned) and lets tests enqueue inbound frames. Reuses the same
    /// unbounded-inbound + receive-count machinery shape as <see cref="ScriptedConnection"/>.
    /// </summary>
    private sealed class RecordingChannel : IRpcChannel
    {
        private readonly System.Threading.Channels.Channel<Payload> _inbound =
            System.Threading.Channels.Channel.CreateUnbounded<Payload>(
                new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        private readonly ConcurrentQueue<(int MessageId, MessageType Type)> _sent = new();
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource<int[]> Completion)> _idWaiters = new();
        private readonly List<(MessageType Type, TaskCompletionSource<(int, MessageType)> Completion)> _typeWaiters = new();
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "recording://remote";

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (MessageFramer.TryReadFrameHeader(data, out var messageId, out var type))
            {
                _sent.Enqueue((messageId, type));
                Notify(messageId, type);
            }

            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public async Task<int[]> WaitForSentFrameIdsAsync(int count, TimeSpan timeout)
        {
            TaskCompletionSource<int[]> completion;
            lock (_gate)
            {
                var ids = RequestIds();
                if (ids.Length >= count)
                {
                    return ids.Take(count).ToArray();
                }

                completion = new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _idWaiters.Add((count, completion));
            }

            return await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
        }

        public async Task<(int MessageId, MessageType Type)> WaitForFrameOfTypeAsync(MessageType type, TimeSpan timeout)
        {
            TaskCompletionSource<(int, MessageType)> completion;
            lock (_gate)
            {
                foreach (var sent in _sent)
                {
                    if (sent.Type == type)
                    {
                        return sent;
                    }
                }

                completion = new TaskCompletionSource<(int, MessageType)>(TaskCreationOptions.RunContinuationsAsynchronously);
                _typeWaiters.Add((type, completion));
            }

            return await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
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

        private int[] RequestIds() =>
            _sent.Where(s => s.Type == MessageType.Request).Select(s => s.MessageId).ToArray();

        private void Notify(int messageId, MessageType type)
        {
            List<TaskCompletionSource<int[]>>? idReady = null;
            List<(TaskCompletionSource<(int, MessageType)>, int)>? typeReady = null;
            lock (_gate)
            {
                if (type == MessageType.Request)
                {
                    var ids = RequestIds();
                    for (var i = _idWaiters.Count - 1; i >= 0; i--)
                    {
                        if (ids.Length >= _idWaiters[i].Count)
                        {
                            idReady ??= new List<TaskCompletionSource<int[]>>();
                            idReady.Add(_idWaiters[i].Completion);
                            _idWaiters.RemoveAt(i);
                        }
                    }
                }

                for (var i = _typeWaiters.Count - 1; i >= 0; i--)
                {
                    if (_typeWaiters[i].Type == type)
                    {
                        typeReady ??= new List<(TaskCompletionSource<(int, MessageType)>, int)>();
                        typeReady.Add((_typeWaiters[i].Completion, messageId));
                        _typeWaiters.RemoveAt(i);
                    }
                }
            }

            if (idReady is not null)
            {
                var snapshot = RequestIds();
                foreach (var w in idReady)
                {
                    w.TrySetResult(snapshot);
                }
            }

            if (typeReady is not null)
            {
                foreach (var (completion, id) in typeReady)
                {
                    completion.TrySetResult((id, type));
                }
            }
        }
    }

    /// <summary>An <see cref="IRpcChannel"/> whose <see cref="SendAsync"/> always throws.</summary>
    private sealed class ThrowingSendChannel : IRpcChannel
    {
        private readonly Exception _error;
        private int _disposed;

        public ThrowingSendChannel(Exception error) => _error = error;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "throwing://remote";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.FromException(_error);

        public Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<Payload>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetResult(Payload.Empty));
            return tcs.Task;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            return default;
        }
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> whose send can be toggled to fail; otherwise it behaves like a
    /// scripted connection (no-op send, inbound queue) so the peer remains usable after recovery.
    /// </summary>
    private sealed class ToggleSendChannel : IRpcChannel
    {
        private readonly System.Threading.Channels.Channel<Payload> _inbound =
            System.Threading.Channels.Channel.CreateUnbounded<Payload>(
                new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        private int _disposed;

        public bool FailNextSends { get; set; }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "toggle://remote";

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            FailNextSends
                ? Task.FromException(new InvalidOperationException("send disabled"))
                : Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
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
}

/// <summary>
/// Test-only framing helper that builds a syntactically valid frame (correct header, envelope-length
/// prefix, exact total length) whose envelope bytes are deliberately not a valid RpcResponse. Used to
/// drive the "malformed response envelope" fault path through the read loop. Lives in the test
/// assembly only; it reuses the public <see cref="MessageFramer"/> header constants.
/// </summary>
internal static class MessageFramerTestExtensions
{
    public static Payload FrameToPayloadWithGarbageEnvelope(int messageId, byte[] garbageEnvelope) =>
        Build(messageId, garbageEnvelope);

    private static Payload Build(int messageId, byte[] garbageEnvelope)
    {
        var totalLength = MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize + garbageEnvelope.Length;
        var frame = Payload.Rent(totalLength);
        var span = frame.Memory.Span;

        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), totalLength);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)MessageType.Response;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            span.Slice(MessageFramer.HeaderSize, MessageFramer.EnvelopeLengthSize),
            garbageEnvelope.Length);
        garbageEnvelope.CopyTo(span.Slice(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize));

        return frame;
    }
}
