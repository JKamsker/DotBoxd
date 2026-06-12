using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

/// <summary>
/// Carries a deserialized response envelope together with the zero-copy payload slice and owner frame.
/// </summary>
internal sealed class ReceivedResponse : IDisposable
{
    private RpcFrame _frame;
    private RpcOutboundStreamSet? _outboundStreams;
    private RpcStreamReceiver? _stream;

    public ReceivedResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        Payload frame,
        RpcStreamReceiver? stream)
        : this(response, payload, new RpcFrame(frame), stream)
    {
    }

    public ReceivedResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        RpcFrame frame,
        RpcStreamReceiver? stream)
    {
        Response = response;
        Payload = payload;
        _frame = frame;
        _stream = stream;
    }

    public RpcResponse Response { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public RpcStreamReceiver? Stream => _stream;

    public void AttachOutboundStreams(RpcOutboundStreamSet streams) =>
        _outboundStreams = streams;

    public RpcOutboundStreamSet? DetachOutboundStreams() =>
        Interlocked.Exchange(ref _outboundStreams, null);

    public RpcStreamReceiver? DetachStream() =>
        Interlocked.Exchange(ref _stream, null);

    public void Dispose()
    {
        _frame.Dispose();
        if (Interlocked.Exchange(ref _outboundStreams, null) is { } streams)
        {
            _ = streams.DisposeAsync();
        }

        if (Interlocked.Exchange(ref _stream, null) is { } stream)
        {
            stream.Cancel();
        }
    }

    public static void DisposeWhenAvailable(Task<ReceivedResponse> task)
    {
        if (task.IsCompleted)
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                task.Result.Dispose();
            }

            return;
        }

        _ = task.ContinueWith(
            static t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    t.Result.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
