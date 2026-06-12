using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov.PeerInbound;

public sealed class PeerInboundCancellationOptionsTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task DisableInboundRequestCancellation_NonStreamingCancelFrameIsIgnored()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        var dispatcher = new ReleasableNonStreamingDispatcher();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    DisableInboundRequestCancellation = true,
                    InboundQueueCapacity = null,
                    RequestTimeout = TestTimeout,
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        using (var requestFrame = CreateRequestFrame(serializer, 7, dispatcher.ServiceName, "Wait"))
        {
            await client.SendAsync(requestFrame.Memory).WaitAsync(TestTimeout);
        }

        var token = await dispatcher.ObservedToken.Task.WaitAsync(TestTimeout);
        Assert.False(token.CanBeCanceled);

        using (var cancelFrame = MessageFramer.FrameToPayload(7, MessageType.Cancel, ReadOnlySpan<byte>.Empty))
        {
            await client.SendAsync(cancelFrame.Memory).WaitAsync(TestTimeout);
        }

        using (var pingFrame = CreateRequestFrame(serializer, 8, dispatcher.ServiceName, "Ping"))
        {
            await client.SendAsync(pingFrame.Memory).WaitAsync(TestTimeout);
        }

        using (var pingResponse = await client.ReceiveAsync().WaitAsync(TestTimeout))
        {
            AssertResponseHeader(pingResponse, expectedMessageId: 8);
        }

        dispatcher.Release();

        using var waitResponse = await client.ReceiveAsync().WaitAsync(TestTimeout);
        AssertResponseHeader(waitResponse, expectedMessageId: 7);
    }

    [Fact]
    public async Task DisableInboundRequestCancellation_StillTracksDuplicateMessageIds()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new ReleasableNonStreamingDispatcher();
        var protocolError = new TaskCompletionSource<RpcProtocolErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connection.Enqueue(CreateRequestFrame(serializer, 9, dispatcher.ServiceName, "Wait"));
        connection.Enqueue(CreateRequestFrame(serializer, 9, dispatcher.ServiceName, "Ping"));

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    DisableInboundRequestCancellation = true,
                    InboundQueueCapacity = null,
                    RequestTimeout = TestTimeout,
                })
            .Provide((IServiceDispatcher)dispatcher);
        peer.ProtocolError += (_, args) => protocolError.TrySetResult(args);
        peer.Start();

        var token = await dispatcher.ObservedToken.Task.WaitAsync(TestTimeout);
        Assert.False(token.CanBeCanceled);

        var args = await protocolError.Task.WaitAsync(TestTimeout);
        Assert.Equal(9, args.MessageId);
        Assert.Equal(MessageType.Request, args.MessageType);
        Assert.Contains("Duplicate request message id", args.Message);

        dispatcher.Release();
    }

    [Fact]
    public async Task DisableInboundRequestCancellation_DoesNotAffectStreamingCapableDispatchers()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        var dispatcher = new CancellableStreamingCapableDispatcher();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    DisableInboundRequestCancellation = true,
                    InboundQueueCapacity = null,
                    RequestTimeout = TestTimeout,
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        using (var requestFrame = CreateRequestFrame(serializer, 10, dispatcher.ServiceName, "Wait"))
        {
            await client.SendAsync(requestFrame.Memory).WaitAsync(TestTimeout);
        }

        var token = await dispatcher.ObservedToken.Task.WaitAsync(TestTimeout);
        Assert.True(token.CanBeCanceled);

        using (var cancelFrame = MessageFramer.FrameToPayload(10, MessageType.Cancel, ReadOnlySpan<byte>.Empty))
        {
            await client.SendAsync(cancelFrame.Memory).WaitAsync(TestTimeout);
        }

        await dispatcher.Canceled.Task.WaitAsync(TestTimeout);
    }

    private static Payload CreateRequestFrame(
        ISerializer serializer,
        int messageId,
        string service,
        string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest { MessageId = messageId, ServiceName = service, MethodName = method },
            ReadOnlySpan<byte>.Empty);

    private static void AssertResponseHeader(Payload frame, int expectedMessageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var messageId, out var messageType));
        Assert.Equal(expectedMessageId, messageId);
        Assert.Equal(MessageType.Response, messageType);
    }

    private sealed class ReleasableNonStreamingDispatcher :
        IServiceDispatcher,
        INonStreamingServiceDispatcher
    {
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => "NoInboundCancel";

        public TaskCompletionSource<CancellationToken> ObservedToken { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            ObservedToken.TrySetResult(ct);
            if (method == "Ping")
            {
                serializer.Serialize(output, "pong");
                return;
            }

            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
            serializer.Serialize(output, "done");
        }

        public void Release() => _release.TrySetResult(true);
    }

    private sealed class CancellableStreamingCapableDispatcher : IServiceDispatcher
    {
        public string ServiceName => "StreamingCapableCancel";

        public TaskCompletionSource<CancellationToken> ObservedToken { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            ObservedToken.TrySetResult(ct);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
