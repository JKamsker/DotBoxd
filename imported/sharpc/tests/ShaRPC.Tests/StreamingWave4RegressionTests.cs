using System.Buffers;
using System.IO.Pipelines;
using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingWave4RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ReceiverCancel_DoesNotWaitForCancelFrameSend()
    {
        var serializer = new MessagePackRpcSerializer();
        var cancelSendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streams = new RpcStreamManager(
            serializer,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                if (type == MessageType.StreamCancel)
                {
                    cancelSendStarted.TrySetResult();
                    return neverComplete.Task;
                }

                return Task.CompletedTask;
            },
            exceptionTransformer: null);
        var receiver = streams.RegisterInboundResponse(
            new RpcStreamHandle(602, RpcStreamKind.Binary),
            CancellationToken.None);

        await receiver.CancelAsync().AsTask().WaitAsync(TestTimeout);

        await cancelSendStarted.Task.WaitAsync(TestTimeout);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public async Task RegisteredOutboundSet_DisposedBeforeStart_DisposesOwnedSources()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var streamHandle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var pipeHandle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var source = new TrackingStream(new byte[] { 1, 2, 3 });
        var pipe = new Pipe();
        var outbound = streams.RegisterOutbound(
            new[]
            {
                RpcStreamAttachment.FromStream(streamHandle, source, leaveOpen: false),
                RpcStreamAttachment.FromPipe(pipeHandle, pipe, completeReader: true),
            },
            CancellationToken.None);

        await outbound.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.True(source.Disposed);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipe.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    [Fact]
    public async Task RegisteredOutboundSet_DisposedBeforeStart_RemovesSenderWhenSourceDisposeThrows()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var source = new ThrowingDisposeStream(new byte[] { 1 });
        var outbound = streams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromStream(handle, source, leaveOpen: false) },
            CancellationToken.None);

        await outbound.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.True(source.DisposeAttempted);
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    [Fact]
    public async Task StartedOutboundSet_DisposeCancelsStalledStreamCompleteSend()
    {
        var serializer = new MessagePackRpcSerializer();
        var completeSendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streams = new RpcStreamManager(
            serializer,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                if (type == MessageType.StreamComplete)
                {
                    completeSendStarted.TrySetResult();
                    return neverComplete.Task;
                }

                return Task.CompletedTask;
            },
            exceptionTransformer: null);
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        await using var outbound = streams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromStream(handle, new MemoryStream()) },
            CancellationToken.None);
        outbound.Start();
        await completeSendStarted.Task.WaitAsync(TestTimeout);

        await outbound.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.Equal(0, streams.OutboundSenderCount);
    }

    [Fact]
    public async Task InitialCreditSendFailure_ReportsAndRemovesReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        var diagnostics = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("credit send failed");
        void OnDiagnostic(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (args.Operation == "Stream credit notification failed" &&
                ReferenceEquals(args.Error, failure))
            {
                diagnostics.TrySetResult(args);
            }
        }

        RpcDiagnostics.Error += OnDiagnostic;
        try
        {
            var streams = new RpcStreamManager(
                serializer,
                (frame, ct) =>
                {
                    Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                    if (type == MessageType.StreamCredit)
                    {
                        throw failure;
                    }

                    return Task.CompletedTask;
                },
                exceptionTransformer: null);

            streams.RegisterInboundResponse(new RpcStreamHandle(603, RpcStreamKind.Binary), CancellationToken.None);

            var diagnostic = await diagnostics.Task.WaitAsync(TestTimeout);
            Assert.Same(failure, diagnostic.Error);
            await WaitUntilAsync(() => streams.InboundReceiverCount == 0);
        }
        finally
        {
            RpcDiagnostics.Error -= OnDiagnostic;
        }
    }

    [Fact]
    public async Task UndeclaredPayloadStreamHandle_ReturnsProtocolErrorWithoutReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var dispatcher = new UndeclaredStreamHandleDispatcher();
        var sentError = new TaskCompletionSource<RpcResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions { InboundQueueCapacity = null },
            streams,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrame(
                    frame,
                    out _,
                    out var messageType,
                    out var envelope,
                    out _));
                if (messageType == MessageType.Error)
                {
                    sentError.TrySetResult(serializer.Deserialize<RpcResponse>(envelope));
                }

                return Task.CompletedTask;
            },
            protocolError: static (_, _, _, _) => { },
            dispatchError: static (_, _) => { });
        inbound.AddDispatcher(dispatcher);
        var undeclaredHandle = new RpcStreamHandle(800, RpcStreamKind.Binary);
        using var body = serializer.SerializeToPayload(undeclaredHandle);
        var frame = MessageFramer.FrameMessage(
            serializer,
            12,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = 12,
                ServiceName = dispatcher.ServiceName,
                MethodName = "Go",
            },
            body.Memory.Span);

        var accepted = await inbound.AcceptRequestAsync(frame, 12, CancellationToken.None);
        if (!accepted)
        {
            frame.Dispose();
        }

        Assert.True(accepted);
        await dispatcher.Invoked.Task.WaitAsync(TestTimeout);
        var response = await sentError.Task.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => inbound.ActiveInboundCount == 0);

        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.ProtocolError, response.ErrorType);
        Assert.Contains("was not declared by the request", response.ErrorMessage);
        Assert.False(dispatcher.Acquired.Task.IsCompleted);
        Assert.Equal(0, streams.InboundReceiverCount);
        Assert.Equal(0, inbound.ActiveInboundCount);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token).ConfigureAwait(false);
        }
    }

    private sealed class UndeclaredStreamHandleDispatcher : IServiceDispatcher
    {
        public string ServiceName => "Undeclared";

        public TaskCompletionSource Invoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Acquired { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            Invoked.TrySetResult();
            _ = streaming.GetStream(serializer.Deserialize<RpcStreamHandle>(payload));
            Acquired.TrySetResult();
            throw new InvalidOperationException("Malformed streamed payload.");
        }
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

    private sealed class ThrowingDisposeStream : MemoryStream
    {
        public ThrowingDisposeStream(byte[] buffer)
            : base(buffer)
        {
        }

        public bool DisposeAttempted { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("Dispose failed.");
        }

        public override ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("Dispose failed.");
        }
    }
}
