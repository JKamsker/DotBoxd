using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingWave13RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void DeclaredInboundStreamHandle_CannotBeClaimedTwice()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var handle = new RpcStreamHandle(17_000, RpcStreamKind.Items);
        streams.RegisterInbound(new[] { handle }, CancellationToken.None);
        var context = new RpcStreamingContext(
            streams,
            serializer,
            CancellationToken.None,
            new[] { handle });

        _ = context.GetAsyncEnumerable<int>(handle);
        var error = Assert.Throws<ShaRpcProtocolException>(
            () => context.GetAsyncEnumerable<int>(handle));

        Assert.Contains("already claimed", error.Message);
        Assert.Equal(1, streams.InboundReceiverCount);
        streams.RemoveInbound(handle.StreamId);
    }

    [Fact]
    public async Task NonStreamingInvoke_RejectsStreamedResponseAndRemovesReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcPeerOutboundInvoker? invoker = null;
        var streams = CreateStreamManager(serializer);
        invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { RequestTimeout = TestTimeout },
            ensureStarted: static () => { },
            SendAndCompleteAsync,
            streams);

        var error = await Assert.ThrowsAsync<ShaRpcProtocolException>(
            () => invoker.InvokeAsync<int>("Svc", "Streamed"));

        Assert.Contains("non-streaming invocation", error.Message);
        Assert.Equal(0, streams.InboundReceiverCount);

        Task SendAndCompleteAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var messageId, out var type));
            if (type != MessageType.Request)
            {
                return Task.CompletedTask;
            }

            var response = MessageFramer.FrameMessage(
                serializer,
                messageId,
                MessageType.Response,
                new RpcResponse
                {
                    MessageId = messageId,
                    IsSuccess = true,
                    Stream = new RpcStreamHandle(17_100, RpcStreamKind.Binary),
                },
                ReadOnlySpan<byte>.Empty);
            if (!invoker!.TryCompleteResponse(messageId, response))
            {
                response.Dispose();
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task StreamingInvoke_RejectsStreamedResponseWithPayloadAndRemovesReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcPeerOutboundInvoker? invoker = null;
        var streams = CreateStreamManager(serializer);
        invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { RequestTimeout = TestTimeout },
            ensureStarted: static () => { },
            SendAndCompleteAsync,
            streams);

        var error = await Assert.ThrowsAsync<ShaRpcProtocolException>(
            () => invoker.InvokeAsyncEnumerableAsync<int>("Svc", "Mixed"));

        Assert.Contains("payload must be empty", error.Message);
        Assert.Equal(0, streams.InboundReceiverCount);

        Task SendAndCompleteAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var messageId, out var type));
            if (type != MessageType.Request)
            {
                return Task.CompletedTask;
            }

            using var body = serializer.SerializeToPayload(123);
            var response = MessageFramer.FrameMessage(
                serializer,
                messageId,
                MessageType.Response,
                new RpcResponse
                {
                    MessageId = messageId,
                    IsSuccess = true,
                    Stream = new RpcStreamHandle(17_200, RpcStreamKind.Items),
                },
                body.Memory.Span);
            if (!invoker!.TryCompleteResponse(messageId, response))
            {
                response.Dispose();
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CanceledInboundTombstoneOverflow_BoundsStateAndRejectsNewInboundStreams()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var diagnostic = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnDiagnostic(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (args.Operation == "Canceled inbound stream tracking failed")
            {
                diagnostic.TrySetResult(args);
            }
        }

        RpcDiagnostics.Error += OnDiagnostic;
        try
        {
            for (var i = 0; i <= RpcCanceledInboundStreams.Capacity; i++)
            {
                var handle = new RpcStreamHandle(18_000 + i, RpcStreamKind.Binary);
                var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
                await receiver.CancelAsync();
            }

            await diagnostic.Task.WaitAsync(TestTimeout);
            Assert.Equal(0, streams.InboundReceiverCount);
            Assert.Equal(RpcCanceledInboundStreams.Capacity, streams.CanceledInboundCount);

            var error = Assert.Throws<ShaRpcProtocolException>(() =>
                streams.RegisterInboundResponse(
                    new RpcStreamHandle(19_500, RpcStreamKind.Binary),
                    CancellationToken.None));
            Assert.Contains("tombstone capacity was exceeded", error.Message);
        }
        finally
        {
            RpcDiagnostics.Error -= OnDiagnostic;
        }
    }

    private static RpcStreamManager CreateStreamManager(MessagePackRpcSerializer serializer) =>
        new(serializer, SendNoopAsync, exceptionTransformer: null);

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
