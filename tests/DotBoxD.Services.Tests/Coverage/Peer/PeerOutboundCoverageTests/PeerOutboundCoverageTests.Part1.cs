using System.Buffers;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Support;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed partial class PeerOutboundCoverageTests
{
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

        var ex = await Assert.ThrowsAsync<ServiceTimeoutException>(() => call.WaitAsync(Timeout));
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
    public async Task InvokeAsync_ResponseEnvelopeMessageIdMismatch_FaultsRequestWithProtocolException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);

        var payloadWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(payloadWriter, "tampered");
        channel.Enqueue(MessageFramer.FrameMessage(
            serializer,
            messageId: 1,
            MessageType.Response,
            new RpcResponse { MessageId = 99, IsSuccess = true },
            payloadWriter.WrittenSpan));

        var ex = await Assert.ThrowsAsync<ServiceProtocolException>(() => call.WaitAsync(Timeout));
        Assert.Contains("message id", ex.Message);
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

        var ex = await Assert.ThrowsAsync<ServiceTimeoutException>(
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

        // DisposeCoreAsync -> FailPending(ServiceConnectionException("Connection closed.")).
        var ex = await Assert.ThrowsAsync<ServiceConnectionException>(() => call.WaitAsync(Timeout));
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

        await Assert.ThrowsAsync<ServiceConnectionException>(() => call.WaitAsync(Timeout));

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
        await using var channel = new ThrowingSendChannel(new ServiceConnectionException("send-broke"));
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // _sender.SendAsync -> _channel.SendAsync throws; SendFrameAndAwaitAsync never sets requestSent,
        // and the exception propagates out of InvokeAsync after releasing the reserved slot.
        var ex = await Assert.ThrowsAsync<ServiceConnectionException>(
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
        // which marks the peer closed and FailPending(ServiceConnectionException("Connection closed.")).
        channel.Enqueue(Payload.Empty);

        var ex = await Assert.ThrowsAsync<ServiceConnectionException>(() => inFlight.WaitAsync(Timeout));
        Assert.Contains("closed", ex.Message);

        // Once closed, the IsConnected projection flips and any new send fast-fails through the sender's
        // closed-guard rather than parking. Poll briefly for the closed state to settle (set on the read
        // loop) without a fixed sleep.
        await WaitUntilAsync(() => !peer.IsConnected, Timeout);

        await Assert.ThrowsAsync<ServiceConnectionException>(
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

}
