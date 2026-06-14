using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingOutboundReservationRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StreamedInvoke_PreCanceledBeforePendingReservation_ReleasesStreamReservation()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions(),
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var source = new TrackingStream(new byte[] { 1 });
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, source, leaveOpen: false),
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            invoker.InvokeAsync<RpcStreamHandle, int>(
                "Svc",
                "Upload",
                handle,
                attachments,
                cts.Token));

        Assert.True(source.Disposed);
        AssertNoPendingCreditForReleasedReservation(streams, handle.StreamId);
    }

    [Fact]
    public async Task StreamedInvoke_MaxPendingBeforeStreamRegistration_ReleasesStreamReservation()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var sentRequest = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { MaxPendingRequests = 1, RequestTimeout = Timeout },
            ensureStarted: static () => { },
            SendAndHoldAsync,
            streams);
        var pending = invoker.InvokeAsync("Svc", "Hold");
        var pendingId = await sentRequest.Task.WaitAsync(Timeout);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var source = new TrackingStream(new byte[] { 1 });
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, source, leaveOpen: false),
        };

        await Assert.ThrowsAsync<ShaRpcException>(() =>
            invoker.InvokeAsync<RpcStreamHandle, int>(
                "Svc",
                "Upload",
                handle,
                attachments));

        Assert.True(source.Disposed);
        AssertNoPendingCreditForReleasedReservation(streams, handle.StreamId);
        CompleteSuccess(invoker, serializer, pendingId);
        await pending.WaitAsync(Timeout);

        Task SendAndHoldAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var messageId, out _));
            sentRequest.TrySetResult(messageId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task StreamedInvoke_TargetValidationFailure_DisposesOwnedAttachmentSource()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { RequestTimeout = Timeout },
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var source = new TrackingStream(new byte[] { 1 });
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, source, leaveOpen: false),
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            invoker.InvokeAsync<RpcStreamHandle, int>(
                "",
                "Upload",
                handle,
                attachments));

        Assert.True(source.Disposed);
        AssertNoPendingCreditForReleasedReservation(streams, handle.StreamId);
    }

    [Fact]
    public async Task StreamedInvoke_SendFailureBeforePumpStart_DisposesOwnedAttachmentSource()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendThrowsAsync, exceptionTransformer: null);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { RequestTimeout = Timeout },
            ensureStarted: static () => { },
            SendThrowsAsync,
            streams);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var source = new TrackingStream(new byte[] { 1 });
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, source, leaveOpen: false),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync<RpcStreamHandle, int>(
                "Svc",
                "Upload",
                handle,
                attachments));

        Assert.True(source.Disposed);
        Assert.Equal(0, streams.OutboundSenderCount);

        static Task SendThrowsAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
            throw new InvalidOperationException("send failed before pumps started.");
    }

    [Fact]
    public async Task StreamedInvoke_FrameConstructionFailureBeforePumpStart_DisposesOwnedAttachmentSource()
    {
        var serializer = new RequestFailingSerializer(new MessagePackRpcSerializer());
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { RequestTimeout = Timeout },
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var source = new TrackingStream(new byte[] { 1 });
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, source, leaveOpen: false),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync<FailingRequest, int>(
                "Svc",
                "Upload",
                new FailingRequest(handle),
                attachments));

        Assert.True(source.Disposed);
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    [Fact]
    public async Task StreamedInvoke_OutboundRegistrationFailure_DisposesOwnedAttachmentSources()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { RequestTimeout = Timeout },
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var first = new TrackingStream(new byte[] { 1 });
        var second = new TrackingStream(new byte[] { 2 });
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, first, leaveOpen: false),
            RpcStreamAttachment.FromStream(handle, second, leaveOpen: false),
        };

        await Assert.ThrowsAsync<ShaRpcProtocolException>(() =>
            invoker.InvokeAsync<(RpcStreamHandle, RpcStreamHandle), int>(
                "Svc",
                "Upload",
                (handle, handle),
                attachments));

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    private static void CompleteSuccess(
        RpcPeerOutboundInvoker invoker,
        MessagePackRpcSerializer serializer,
        int messageId)
    {
        var response = MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Response,
            new RpcResponse { MessageId = messageId, IsSuccess = true },
            ReadOnlySpan<byte>.Empty);
        if (!invoker.TryCompleteResponse(messageId, response))
        {
            response.Dispose();
        }
    }

    private static void AssertNoPendingCreditForReleasedReservation(
        RpcStreamManager streams,
        int streamId)
    {
        using var credit = RpcRawFrame.FrameInt32(streamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));
        Assert.Equal(0, streams.PendingCreditCount);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private readonly record struct FailingRequest(RpcStreamHandle Handle);

    private sealed class RequestFailingSerializer : ISerializer
    {
        private readonly ISerializer _inner;

        public RequestFailingSerializer(ISerializer inner) => _inner = inner;

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (value is FailingRequest)
            {
                throw new InvalidOperationException("Request serialization failed.");
            }

            _inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) =>
            _inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            _inner.Deserialize(data, type);
    }

    private sealed class TrackingStream : MemoryStream
    {
        public TrackingStream(byte[] buffer)
            : base(buffer)
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            Disposed = true;
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
