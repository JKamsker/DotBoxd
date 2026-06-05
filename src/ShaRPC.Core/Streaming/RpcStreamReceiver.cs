using System.Threading.Channels;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcStreamReceiver
{
    private readonly Channel<RpcStreamChunk> _chunks;
    private readonly RpcStreamManager _manager;
    private RpcOutboundStreamSet? _outboundStreams;
    private int _completed;

    public RpcStreamReceiver(RpcStreamManager manager, RpcStreamHandle handle)
    {
        _manager = manager;
        Handle = handle;
        _chunks = Channel.CreateBounded<RpcStreamChunk>(
            new BoundedChannelOptions(RpcStreamManager.WindowSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
            });
    }

    public RpcStreamHandle Handle { get; }

    public void AttachOutboundStreams(RpcOutboundStreamSet streams)
    {
        if (Volatile.Read(ref _completed) != 0)
        {
            _ = streams.DisposeAsync();
            return;
        }

        if (Interlocked.Exchange(ref _outboundStreams, streams) is { } previous)
        {
            _ = previous.DisposeAsync();
        }

        if (Volatile.Read(ref _completed) != 0 &&
            Interlocked.CompareExchange(ref _outboundStreams, null, streams) == streams)
        {
            _ = streams.DisposeAsync();
        }
    }

    public bool TryAccept(Payload frame)
    {
        if (Volatile.Read(ref _completed) != 0)
        {
            return false;
        }

        var chunk = new RpcStreamChunk(
            this,
            frame,
            frame.Memory.Slice(MessageFramer.HeaderSize));
        if (_chunks.Writer.TryWrite(chunk))
        {
            return true;
        }

        chunk.Dispose();
        Complete(new InvalidDataException("Stream receiver window was exceeded."));
        return false;
    }

    public async ValueTask<RpcStreamChunk?> ReadChunkAsync(CancellationToken ct)
    {
        try
        {
            while (await _chunks.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                if (_chunks.Reader.TryRead(out var chunk))
                {
                    return chunk;
                }
            }

            await _chunks.Reader.Completion.ConfigureAwait(false);
            _manager.RemoveCompletedInbound(Handle.StreamId);
            return null;
        }
        catch
        {
            if (_chunks.Reader.Completion.IsCompleted)
            {
                _manager.RemoveCompletedInbound(Handle.StreamId);
            }

            throw;
        }
    }

    public void Complete(Exception? error = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _chunks.Writer.TryComplete(error);
        if (Interlocked.Exchange(ref _outboundStreams, null) is { } streams)
        {
            _ = streams.DisposeAsync();
        }
    }

    public void Cancel()
    {
        if (Volatile.Read(ref _completed) == 0)
        {
            _ = SendCancelBestEffortAsync();
        }

        Abort(new OperationCanceledException());
        _manager.RemoveInbound(Handle.StreamId);
    }

    public async ValueTask CancelAsync()
    {
        try
        {
            if (Volatile.Read(ref _completed) == 0)
            {
                await SendCancelBestEffortAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            Abort(new OperationCanceledException());
            _manager.RemoveInbound(Handle.StreamId);
        }
    }

    internal void Abort(Exception? error = null)
    {
        Complete(error);
        while (_chunks.Reader.TryRead(out var chunk))
        {
            chunk.Dispose();
        }
    }

    private async Task SendCancelBestEffortAsync()
    {
        try
        {
            await _manager.SendCancelAsync(Handle.StreamId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Stream cancel notification failed", ex);
        }
    }

    public void ReleaseCredit()
    {
        if (Volatile.Read(ref _completed) == 0)
        {
            _ = _manager.SendCreditAsync(Handle.StreamId, 1, CancellationToken.None);
        }
    }
}
