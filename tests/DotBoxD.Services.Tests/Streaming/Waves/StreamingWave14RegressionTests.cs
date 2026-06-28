using System.Buffers;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Waves;

public sealed class StreamingWave14RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ForwardedRemoteStreamError_PreservesOriginalRemoteType()
    {
        var serializer = new MessagePackRpcSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TestTimeout })
            .Provide((IServiceDispatcher)new ForwardingUploadDispatcher())
            .Start();
        await using var client = RpcPeer
            .Over(
                clientConnection,
                serializer,
                new RpcPeerOptions
                {
                    RequestTimeout = TestTimeout,
                    ExceptionTransformer = static ex => ex is InvalidOperationException
                        ? new RpcErrorInfo("client upload failed", "ClientUploadBoom")
                        : null,
                })
            .Start();

        var upload = client.ReserveStream(RpcStreamKind.Items);
        var attachments = new[]
        {
            RpcStreamAttachment.FromAsyncEnumerable(upload, FailingUpload()),
        };
        var response = client.InvokeAsyncEnumerable<RpcStreamHandle, int>(
            "StreamingForward",
            "Forward",
            upload,
            attachments);
        var received = new List<int>();

        var error = await Assert.ThrowsAsync<RemoteServiceException>(
            () => DrainAsync(response, received));

        Assert.Equal(new[] { 1 }, received);
        Assert.Equal("client upload failed", error.Message);
        Assert.Equal("ClientUploadBoom", error.RemoteExceptionType);
    }

    private static async Task DrainAsync(IAsyncEnumerable<int> source, List<int> received)
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        await foreach (var item in source.WithCancellation(cts.Token))
        {
            received.Add(item);
        }
    }

    private static async IAsyncEnumerable<int> FailingUpload(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return 1;
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("raw client detail");
    }

    private sealed class ForwardingUploadDispatcher : IServiceDispatcher
    {
        public string ServiceName => "StreamingForward";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException("Streaming test uses the streaming dispatch overload.");

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            if (!string.Equals(method, "Forward", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected method: " + method);
            }

            var upload = serializer.Deserialize<RpcStreamHandle>(payload);
            streaming.SetResponse(ForwardAsync(streaming.GetAsyncEnumerable<int>(upload), ct));
            return Task.CompletedTask;
        }

        private static async IAsyncEnumerable<int> ForwardAsync(
            IAsyncEnumerable<int> source,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }
}
