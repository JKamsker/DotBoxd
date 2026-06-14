using System.Buffers;
using System.Collections.Concurrent;
using DotBoxd.Services;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Serialization;
using DotBoxd.Services.Server;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

public sealed class StreamingResponseBuilderRegressionTests
{
    [Fact]
    public async Task DispatchCanceledAfterSetResponse_AbandonsReservationAndDisposesSource()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var dispatcher = new CancelAfterSetResponseDispatcher();
        var builder = CreateBuilder(serializer, dispatcher);
        var context = new RpcStreamingContext(streams, serializer, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            builder.BuildAsync(
                CreateRequest(dispatcher.ServiceName),
                messageId: 1,
                ReadOnlyMemory<byte>.Empty,
                new InstanceRegistry(),
                context,
                cts.Token).AsTask());

        Assert.True(dispatcher.ResponseStream.Disposed);
        AssertNoPendingCreditForReleasedReservation(streams, streamId: 1);
    }

    [Fact]
    public async Task ResponseFrameSerializationFailureAfterSetResponse_AbandonsResponse()
    {
        var serializer = new ResponseStreamFailingSerializer(new MessagePackRpcSerializer());
        var streams = CreateStreamManager(serializer);
        var dispatcher = new SetResponseDispatcher();
        var builder = CreateBuilder(serializer, dispatcher);
        var context = new RpcStreamingContext(streams, serializer, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(
                CreateRequest(dispatcher.ServiceName),
                messageId: 1,
                ReadOnlyMemory<byte>.Empty,
                new InstanceRegistry(),
                context,
                CancellationToken.None).AsTask());

        Assert.True(dispatcher.ResponseStream.Disposed);
        AssertNoPendingCreditForReleasedReservation(streams, streamId: 1);
    }

    [Fact]
    public async Task DispatchFailureAfterSetResponse_WhenResponseDisposeThrows_ReturnsDispatchError()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var dispatcher = new ThrowAfterSetResponseDispatcher();
        var builder = CreateBuilder(serializer, dispatcher);
        var context = new RpcStreamingContext(streams, serializer, CancellationToken.None);

        using var result = await builder.BuildAsync(
            CreateRequest(dispatcher.ServiceName),
            messageId: 1,
            ReadOnlyMemory<byte>.Empty,
            new InstanceRegistry(),
            context,
            CancellationToken.None);

        Assert.Null(result.Stream);
        Assert.True(dispatcher.ResponseStream.DisposeAttempted);
        AssertNoPendingCreditForReleasedReservation(streams, streamId: 1);
        Assert.True(MessageFramer.TryReadFrame(
            result.FrameMemory,
            out _,
            out var messageType,
            out var envelope,
            out _));
        Assert.Equal(MessageType.Error, messageType);
        var response = serializer.Deserialize<RpcResponse>(envelope);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.InternalError, response.ErrorType);
        Assert.Equal("Internal error.", response.ErrorMessage);
    }

    private static RpcDispatchResponseBuilder CreateBuilder(
        ISerializer serializer,
        IServiceDispatcher dispatcher)
    {
        var dispatchers = new ConcurrentDictionary<string, IServiceDispatcher>();
        Assert.True(dispatchers.TryAdd(dispatcher.ServiceName, dispatcher));
        return new RpcDispatchResponseBuilder(serializer, dispatchers);
    }

    private static RpcRequest CreateRequest(string serviceName) =>
        new()
        {
            MessageId = 1,
            ServiceName = serviceName,
            MethodName = "Go",
        };

    private static RpcStreamManager CreateStreamManager(ISerializer serializer) =>
        new(serializer, SendNoopAsync, exceptionTransformer: null);

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

    private sealed class CancelAfterSetResponseDispatcher : IServiceDispatcher
    {
        public string ServiceName => "CancelAfterSetResponse";

        public TrackingStream ResponseStream { get; } = new();

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            streaming.SetResponse(ResponseStream);
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class SetResponseDispatcher : IServiceDispatcher
    {
        public string ServiceName => "SetResponse";

        public TrackingStream ResponseStream { get; } = new();

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            streaming.SetResponse(ResponseStream);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowAfterSetResponseDispatcher : IServiceDispatcher
    {
        public string ServiceName => "ThrowAfterSetResponse";

        public ThrowingDisposeStream ResponseStream { get; } = new();

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            streaming.SetResponse(ResponseStream);
            throw new InvalidOperationException("Dispatch failed.");
        }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingStream : MemoryStream
    {
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

    private sealed class ResponseStreamFailingSerializer : ISerializer
    {
        private readonly ISerializer _inner;

        public ResponseStreamFailingSerializer(ISerializer inner) => _inner = inner;

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (value is RpcResponse response && response.Stream is not null)
            {
                throw new InvalidOperationException("Response stream serialization failed.");
            }

            _inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) =>
            _inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            _inner.Deserialize(data, type);
    }
}
