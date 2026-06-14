using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using DotBoxd.Services;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Serialization;
using DotBoxd.Services.Server;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;

namespace DotBoxd.Services.Tests;

internal sealed class StreamingTestDispatcher : IServiceDispatcher
{
    private readonly TaskCompletionSource _downloadGate =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _numbersGate =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string ServiceName => "Streaming";

    public TaskCompletionSource DownloadSourceRead { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource DownloadStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource NumbersCanceled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource UploadBytesRead { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource UploadItemsRead { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource UploadPipeRead { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource UploadStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleaseDownload() => _downloadGate.TrySetResult();

    public void ReleaseNumbers() => _numbersGate.TrySetResult();

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Streaming tests use the streaming dispatch overload.");

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        IRpcStreamingContext streaming,
        CancellationToken ct = default) =>
        method switch
        {
            "Numbers" => SetNumbersAsync(streaming, ct),
            "Download" => SetDownload(streaming),
            "Pipe" => SetPipe(streaming),
            "Upload" => DispatchUploadAsync(payload, serializer, streaming, output, ct),
            "Ping" => SerializePing(serializer, output),
            _ => throw new InvalidOperationException("Unexpected method: " + method),
        };

    private Task SetNumbersAsync(IRpcStreamingContext streaming, CancellationToken ct)
    {
        streaming.SetResponse(NumbersAsync(ct));
        return Task.CompletedTask;
    }

    private Task SetDownload(IRpcStreamingContext streaming)
    {
        DownloadStarted.TrySetResult();
        streaming.SetResponse(new StreamingTestGatedStream(_downloadGate.Task, DownloadSourceRead));
        return Task.CompletedTask;
    }

    private static Task SetPipe(IRpcStreamingContext streaming)
    {
        streaming.SetResponse(CreatePipe());
        return Task.CompletedTask;
    }

    private static Task SerializePing(ISerializer serializer, IBufferWriter<byte> output)
    {
        serializer.Serialize(output, 42);
        return Task.CompletedTask;
    }

    private async IAsyncEnumerable<int> NumbersAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            yield return 1;
            await _numbersGate.Task.WaitAsync(ct).ConfigureAwait(false);
            yield return 2;
        }
        finally
        {
            NumbersCanceled.TrySetResult();
        }
    }

    private async Task DispatchUploadAsync(
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IRpcStreamingContext streaming,
        IBufferWriter<byte> output,
        CancellationToken ct)
    {
        UploadStarted.TrySetResult();
        var handles = serializer.Deserialize<(RpcStreamHandle, RpcStreamHandle, RpcStreamHandle)>(payload);
        var sum = await SumStreamAsync(streaming.GetStream(handles.Item1), ct).ConfigureAwait(false);
        UploadBytesRead.TrySetResult();

        await foreach (var item in streaming.GetAsyncEnumerable<int>(handles.Item2).WithCancellation(ct))
        {
            sum += item;
        }
        UploadItemsRead.TrySetResult();

        sum += await SumPipeAsync(streaming.GetPipe(handles.Item3), ct).ConfigureAwait(false);
        UploadPipeRead.TrySetResult();
        serializer.Serialize(output, sum);
    }

    private static async Task<int> SumStreamAsync(Stream bytes, CancellationToken ct)
    {
        await using (bytes.ConfigureAwait(false))
        {
            var sum = 0;
            var buffer = new byte[16];
            int read;
            while ((read = await bytes.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                sum += buffer.AsSpan(0, read).ToArray().Sum(static b => b);
            }

            return sum;
        }
    }

    private static async Task<int> SumPipeAsync(Pipe pipe, CancellationToken ct)
    {
        var sum = 0;
        while (true)
        {
            var result = await pipe.Reader.ReadAsync(ct).ConfigureAwait(false);
            foreach (var segment in result.Buffer)
            {
                sum += segment.Span.ToArray().Sum(static b => b);
            }

            pipe.Reader.AdvanceTo(result.Buffer.End);
            if (result.IsCompleted)
            {
                break;
            }
        }

        await pipe.Reader.CompleteAsync().ConfigureAwait(false);
        return sum;
    }

    private static Pipe CreatePipe()
    {
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { 9, 10, 11 });
        _ = pipe.Writer.CompleteAsync();
        return pipe;
    }
}

internal sealed class StreamingTestGatedStream : Stream
{
    private readonly Task _gate;
    private readonly TaskCompletionSource _readStarted;
    private int _readCount;

    public StreamingTestGatedStream(Task gate, TaskCompletionSource readStarted)
    {
        _gate = gate;
        _readStarted = readStarted;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = Interlocked.Increment(ref _readCount);
        if (read == 1)
        {
            _readStarted.TrySetResult();
            new byte[] { 1, 2, 3, 4 }.CopyTo(buffer);
            return 4;
        }

        if (read == 2)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            new byte[] { 5, 6, 7, 8 }.CopyTo(buffer);
            return 4;
        }

        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

internal sealed class StreamingPeerPair : IAsyncDisposable
{
    private StreamingPeerPair(RpcPeer client, RpcPeer server)
    {
        Client = client;
        Server = server;
    }

    public RpcPeer Client { get; }

    public RpcPeer Server { get; }

    public static Task<StreamingPeerPair> StartAsync(IServiceDispatcher dispatcher)
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(10) })
            .Provide(dispatcher)
            .Start();
        var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(10) })
            .Start();
        return Task.FromResult(new StreamingPeerPair(client, server));
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }
}
