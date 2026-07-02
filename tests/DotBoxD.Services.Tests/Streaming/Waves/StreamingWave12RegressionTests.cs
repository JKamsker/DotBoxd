using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Waves;

public sealed class StreamingWave12RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ZeroIdStreamComplete_ReportsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var processor = CreateProcessor(
            serializer,
            CreateStreamManager(serializer),
            protocolErrors);
        using var complete = MessageFramer.FrameToPayload(
            0,
            MessageType.StreamComplete,
            ReadOnlySpan<byte>.Empty);

        Assert.True(await processor.ShouldDisposeAsync(complete, CancellationToken.None));

        Assert.Single(protocolErrors, error => error.Contains("Malformed stream complete frame."));
    }

    [Fact]
    public async Task ValidShapedZeroIdStreamError_ReportsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var processor = CreateProcessor(
            serializer,
            CreateStreamManager(serializer),
            protocolErrors);
        using var error = MessageFramer.FrameMessage(
            serializer,
            0,
            MessageType.StreamError,
            new RpcResponse
            {
                MessageId = 0,
                IsSuccess = false,
                ErrorMessage = "remote failed",
                ErrorType = "Remote",
            },
            ReadOnlySpan<byte>.Empty);

        Assert.True(await processor.ShouldDisposeAsync(error, CancellationToken.None));

        Assert.Single(protocolErrors, entry => entry.Contains("Malformed stream error frame."));
    }

    [Fact]
    public async Task ZeroIdStreamCancel_ReportsProtocolErrorWithoutOutboundCancelLookup()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var outboundCancelLookups = 0;
        streams.AfterOutboundSenderMissForTest = _ => outboundCancelLookups++;
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        using var cancel = MessageFramer.FrameToPayload(
            0,
            MessageType.StreamCancel,
            ReadOnlySpan<byte>.Empty);

        Assert.True(await processor.ShouldDisposeAsync(cancel, CancellationToken.None));

        Assert.Single(protocolErrors, error => error.Contains("Malformed stream cancel frame."));
        Assert.Equal(0, outboundCancelLookups);
    }

    [Fact]
    public async Task StreamCancelWithTrailingPayload_ReportsProtocolErrorWithoutCancelingOutbound()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = new RpcStreamHandle(16_025, RpcStreamKind.Items);
        streams.ReserveOutbound(handle.StreamId);
        await using var outbound = streams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromAsyncEnumerable(handle, BlockingItems(started, canceled)) },
            CancellationToken.None);
        outbound.Start();
        await started.Task.WaitAsync(TestTimeout);
        using var cancel = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamCancel,
            new byte[] { 1 });

        Assert.True(await processor.ShouldDisposeAsync(cancel, CancellationToken.None));

        Assert.Single(protocolErrors, error => error.Contains("Malformed stream cancel frame."));
        Assert.False(canceled.Task.IsCompleted);
    }

    [Fact]
    public async Task ZeroIdStreamCredit_ReportsProtocolErrorWithoutOutboundCreditLookup()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var outboundSenderMisses = 0;
        var reservedCreditBuffers = 0;
        streams.AfterOutboundSenderMissForTest = _ => outboundSenderMisses++;
        streams.AfterReservedOutboundCreditObservedForTest = _ => reservedCreditBuffers++;
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        using var credit = RpcRawFrame.FrameInt32(0, MessageType.StreamCredit, 1);

        Assert.True(await processor.ShouldDisposeAsync(credit, CancellationToken.None));

        Assert.Single(protocolErrors, error => error.Contains("Malformed stream credit frame."));
        Assert.Equal(0, outboundSenderMisses);
        Assert.Equal(0, reservedCreditBuffers);
        Assert.Equal(0, streams.PendingCreditCount);
    }

    [Fact]
    public async Task ZeroIdStreamItem_ReportsMalformedFrame()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var processor = CreateProcessor(
            serializer,
            CreateStreamManager(serializer),
            protocolErrors);
        using var item = MessageFramer.FrameToPayload(
            0,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });

        Assert.True(await processor.ShouldDisposeAsync(item, CancellationToken.None));

        Assert.Single(protocolErrors, entry => entry.Contains("Malformed stream item frame."));
        Assert.DoesNotContain(protocolErrors, entry => entry.Contains("Unknown stream id."));
    }

    [Fact]
    public async Task UnknownStreamComplete_ReportsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var processor = CreateProcessor(
            serializer,
            CreateStreamManager(serializer),
            protocolErrors);
        using var complete = MessageFramer.FrameToPayload(
            16_050,
            MessageType.StreamComplete,
            ReadOnlySpan<byte>.Empty);

        Assert.True(await processor.ShouldDisposeAsync(complete, CancellationToken.None));

        Assert.Single(protocolErrors, error => error.Contains("Unknown stream id."));
    }

    [Fact]
    public async Task UnknownStreamError_ReportsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var processor = CreateProcessor(
            serializer,
            CreateStreamManager(serializer),
            protocolErrors);
        using var error = MessageFramer.FrameMessage(
            serializer,
            16_060,
            MessageType.StreamError,
            new RpcResponse
            {
                MessageId = 16_060,
                IsSuccess = false,
                ErrorMessage = "remote failed",
                ErrorType = "Remote",
            },
            ReadOnlySpan<byte>.Empty);

        Assert.True(await processor.ShouldDisposeAsync(error, CancellationToken.None));

        Assert.Single(protocolErrors, entry => entry.Contains("Unknown stream id."));
    }

    [Fact]
    public async Task CanceledInboundStreamId_CannotBeRegisteredUntilValidTerminalConsumesTombstone()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(16_000, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        await receiver.CancelAsync();

        var error = Assert.Throws<ServiceProtocolException>(() =>
            streams.RegisterInboundResponse(handle, CancellationToken.None));
        Assert.Contains("awaiting a terminal frame", error.Message);
        Assert.Equal(0, streams.InboundReceiverCount);
        Assert.Equal(1, streams.CanceledInboundCount);

        using (var complete = MessageFramer.FrameToPayload(
                   handle.StreamId,
                   MessageType.StreamComplete,
                   ReadOnlySpan<byte>.Empty))
        {
            Assert.True(await processor.ShouldDisposeAsync(complete, CancellationToken.None));
        }

        Assert.Empty(protocolErrors);
        Assert.Equal(0, streams.CanceledInboundCount);

        var replacement = streams.RegisterInboundResponse(handle, CancellationToken.None);
        using var item = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });
        Assert.False(await processor.ShouldDisposeAsync(item, CancellationToken.None));

        using var chunk = await replacement.ReadChunkAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TestTimeout);
        Assert.NotNull(chunk);
        Assert.Equal(new byte[] { 1, 2, 3 }, chunk.Payload.ToArray());
    }

    [Fact]
    public async Task TerminalBeforeCancel_DoesNotLeaveStaleTombstone()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var handle = new RpcStreamHandle(16_100, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        streams.CompleteInbound(handle.StreamId);
        await receiver.CancelAsync();

        Assert.Equal(0, streams.InboundReceiverCount);
        Assert.Equal(0, streams.CanceledInboundCount);

        var replacement = streams.RegisterInboundResponse(handle, CancellationToken.None);
        Assert.Equal(handle, replacement.Handle);
    }

    private static RpcStreamManager CreateStreamManager(MessagePackRpcSerializer serializer) =>
        new(serializer, SendNoopAsync, exceptionTransformer: null);

    private static RpcPeerFrameProcessor CreateProcessor(
        MessagePackRpcSerializer serializer,
        RpcStreamManager streams,
        List<string> protocolErrors)
    {
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions(),
            streams,
            SendNoopAsync,
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"),
            dispatchError: static (_, _) => { });
        var outbound = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions(),
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        return new RpcPeerFrameProcessor(
            inbound,
            outbound,
            streams,
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"));
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private static async IAsyncEnumerable<int> BlockingItems(
        TaskCompletionSource started,
        TaskCompletionSource canceled,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        started.TrySetResult();
        using var registration = ct.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            canceled);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        yield break;
    }
}
