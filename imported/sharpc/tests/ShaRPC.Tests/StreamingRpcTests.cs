using System.Buffers;
using System.IO.Pipelines;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingRpcTests
{
    [Fact]
    public async Task AsyncEnumerableResponse_YieldsIncrementally_AndDoesNotBlockOtherCalls()
    {
        var service = new StreamingTestDispatcher();
        await using var pair = await StreamingPeerPair.StartAsync(service);

        await using var enumerator = pair.Client
            .InvokeAsyncEnumerable<int>("Streaming", "Numbers")
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, enumerator.Current);

        var ping = await pair.Client.InvokeAsync<int>("Streaming", "Ping")
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, ping);

        service.ReleaseNumbers();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, enumerator.Current);
        Assert.False(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task StreamResponse_ReadsBytesAsProducerReleasesThem()
    {
        var service = new StreamingTestDispatcher();
        await using var pair = await StreamingPeerPair.StartAsync(service);

        var streamTask = pair.Client.InvokeStreamAsync("Streaming", "Download");
        await service.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var stream = await streamTask.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DownloadSourceRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, read);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);

        var delayedRead = stream.ReadAsync(buffer).AsTask();
        Assert.False(delayedRead.IsCompleted);

        var ping = await pair.Client.InvokeAsync<int>("Streaming", "Ping")
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, ping);

        service.ReleaseDownload();
        read = await delayedRead.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, read);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, buffer);
    }

    [Fact]
    public async Task PipeResponse_CanBeReadThroughPipeReader()
    {
        var service = new StreamingTestDispatcher();
        await using var pair = await StreamingPeerPair.StartAsync(service);

        var pipe = await pair.Client.InvokePipeAsync("Streaming", "Pipe")
            .WaitAsync(TimeSpan.FromSeconds(5));
        var result = await pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new byte[] { 9, 10, 11 }, result.Buffer.ToArray());
        pipe.Reader.AdvanceTo(result.Buffer.End);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task StreamedArguments_AreReadIncrementallyByDispatcher()
    {
        var service = new StreamingTestDispatcher();
        await using var pair = await StreamingPeerPair.StartAsync(service);
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { 5, 6 });
        await pipe.Writer.CompleteAsync();

        var bytes = pair.Client.ReserveStream(RpcStreamKind.Binary);
        var items = pair.Client.ReserveStream(RpcStreamKind.Items);
        var pipeHandle = pair.Client.ReserveStream(RpcStreamKind.Binary);
        var request = (bytes, items, pipeHandle);
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(bytes, new MemoryStream(new byte[] { 1, 2, 3 })),
            RpcStreamAttachment.FromAsyncEnumerable(items, UploadItems()),
            RpcStreamAttachment.FromPipe(pipeHandle, pipe),
        };

        var upload = pair.Client
            .InvokeAsync<(RpcStreamHandle, RpcStreamHandle, RpcStreamHandle), int>(
                "Streaming",
                "Upload",
                request,
                attachments);

        await WaitForStageOrUploadAsync(service.UploadStarted.Task, upload, "start");
        await WaitForStageOrUploadAsync(service.UploadBytesRead.Task, upload, "read byte stream");
        await WaitForStageOrUploadAsync(service.UploadItemsRead.Task, upload, "read item stream");
        await WaitForStageOrUploadAsync(service.UploadPipeRead.Task, upload, "read pipe stream");
        var sum = await upload.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1 + 2 + 3 + 10 + 20 + 5 + 6, sum);
    }

    [Fact]
    public async Task StreamManager_DeliversBinaryStreamAfterInitialCredit()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcStreamManager? clientStreams = null;
        RpcStreamManager? serverStreams = null;
        clientStreams = new RpcStreamManager(serializer, SendToServerAsync, exceptionTransformer: null);
        serverStreams = new RpcStreamManager(serializer, SendToClientAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(100, RpcStreamKind.Binary);
        clientStreams.ReserveOutbound(handle.StreamId);
        serverStreams.RegisterInbound(new[] { handle }, CancellationToken.None);
        var receiver = serverStreams.GetRegisteredInbound(handle);

        await using var outbound = clientStreams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromStream(handle, new MemoryStream(new byte[] { 7, 8, 9 })) },
            CancellationToken.None);
        outbound.Start();

        await using var stream = new RpcRemoteStream(receiver);
        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer, CancellationToken.None).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await outbound.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 7, 8, 9 }, buffer);

        Task SendToClientAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            var payload = CopyPayload(frame);
            if (!MessageFramer.TryReadFrameHeader(payload.Memory, out var streamId, out var type))
            {
                payload.Dispose();
                throw new InvalidDataException("Malformed test frame.");
            }

            if (type == MessageType.StreamCredit)
            {
                Assert.True(clientStreams!.TryAddCredit(payload));
                payload.Dispose();
                return Task.CompletedTask;
            }

            payload.Dispose();
            throw new InvalidDataException("Unexpected client frame type: " + type);
        }

        Task SendToServerAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            var payload = CopyPayload(frame);
            if (!MessageFramer.TryReadFrameHeader(payload.Memory, out var streamId, out var type))
            {
                payload.Dispose();
                throw new InvalidDataException("Malformed test frame.");
            }

            switch (type)
            {
                case MessageType.StreamItem:
                    if (!serverStreams!.TryAcceptItem(streamId, payload))
                    {
                        payload.Dispose();
                        throw new InvalidDataException("Unknown test stream.");
                    }
                    return Task.CompletedTask;
                case MessageType.StreamComplete:
                    serverStreams!.CompleteInbound(streamId);
                    payload.Dispose();
                    return Task.CompletedTask;
                default:
                    payload.Dispose();
                    throw new InvalidDataException("Unexpected server frame type: " + type);
            }
        }
    }

    [Fact]
    public async Task DisposingAsyncEnumerableResponse_CancelsRemoteProducer()
    {
        var service = new StreamingTestDispatcher();
        await using var pair = await StreamingPeerPair.StartAsync(service);

        var enumerator = pair.Client
            .InvokeAsyncEnumerable<int>("Streaming", "Numbers")
            .GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        await enumerator.DisposeAsync();

        await service.NumbersCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Payload CopyPayload(ReadOnlyMemory<byte> frame)
    {
        var payload = Payload.Rent(frame.Length);
        frame.CopyTo(payload.Memory);
        return payload;
    }

    private static async Task WaitForStageOrUploadAsync(
        Task stage,
        Task upload,
        string stageName)
    {
        var completed = await Task.WhenAny(stage, upload, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed == stage)
        {
            await stage;
            return;
        }

        if (completed == upload)
        {
            await upload;
            throw new InvalidOperationException("Upload completed before stage: " + stageName);
        }

        throw new TimeoutException("Upload did not reach stage: " + stageName);
    }

    private static async IAsyncEnumerable<int> UploadItems()
    {
        yield return 10;
        await Task.Yield();
        yield return 20;
    }
}
