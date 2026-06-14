using System.Threading.Channels;
using DotBoxd.Services;
using DotBoxd.Services.Buffers;
using DotBoxd.Services.Exceptions;
using DotBoxd.Services.Protocol;

namespace DotBoxd.Services.Streaming;

internal sealed class RpcStreamReceiver
{
    private readonly Channel<RpcStreamChunk> _chunks;
    private readonly RpcStreamManager _manager;
    private readonly object _completionGate = new();
    private RpcOutboundStreamSet? _outboundStreams;
    private int _creditFlushScheduled;
    private int _completed;
    private int _pendingCreditCount;

    public RpcStreamReceiver(RpcStreamManager manager, RpcStreamHandle handle)
    {
        _manager = manager;
        Handle = handle;
        _chunks = Channel.CreateBounded<RpcStreamChunk>(
            new BoundedChannelOptions(RpcStreamManager.WindowSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
    }

    public RpcStreamHandle Handle { get; }
    internal bool IsCompleted => Volatile.Read(ref _completed) != 0;

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

    public RpcStreamAcceptResult TryAccept(Payload frame)
    {
        if (Volatile.Read(ref _completed) != 0)
        {
            frame.Dispose();
            return RpcStreamAcceptResult.Consumed;
        }

        var chunk = new RpcStreamChunk(
            this,
            frame,
            frame.Memory.Slice(MessageFramer.HeaderSize));
        if (_chunks.Writer.TryWrite(chunk))
        {
            return RpcStreamAcceptResult.Accepted;
        }

        chunk.DisposeWithoutCredit();
        Abort(new InvalidDataException("Stream receiver window was exceeded."));
        _manager.RemoveCompletedInbound(this);
        return RpcStreamAcceptResult.Rejected;
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
            _manager.RemoveCompletedInbound(this);
            return null;
        }
        catch
        {
            if (_chunks.Reader.Completion.IsCompleted)
            {
                _manager.RemoveCompletedInbound(this);
            }

            throw;
        }
    }

    public bool Complete(Exception? error = null)
    {
        RpcOutboundStreamSet? streams;
        lock (_completionGate)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return false;
            }

            _chunks.Writer.TryComplete(error);
            streams = Interlocked.Exchange(ref _outboundStreams, null);
        }

        _manager.DecrementActiveInbound();

        if (streams is not null)
        {
            _ = streams.DisposeAsync();
        }

        return true;
    }

    public void Cancel()
    {
        if (CancelCore())
        {
            _ = SendCancelBestEffortAsync();
        }
    }

    public ValueTask CancelAsync()
    {
        Cancel();
        return default;
    }

    internal void Abort(Exception? error = null)
    {
        Complete(error);
        DrainChunks();
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

    private bool CancelCore()
    {
        var sendCancel = TryCompleteForCancel();
        if (!sendCancel)
        {
            _manager.RemoveCompletedInbound(this);
        }

        DrainChunks();
        return sendCancel;
    }

    private bool TryCompleteForCancel()
    {
        RpcOutboundStreamSet? streams;
        lock (_completionGate)
        {
            if (Volatile.Read(ref _completed) != 0)
            {
                return false;
            }

            Interlocked.Exchange(ref _completed, 1);
            try
            {
                _manager.RemoveCanceledInbound(Handle.StreamId);
                _chunks.Writer.TryComplete(new OperationCanceledException());
            }
            catch (Exception ex)
            {
                RpcDiagnostics.Report("Canceled inbound stream tracking failed", ex);
                _chunks.Writer.TryComplete(ex);
            }

            streams = Interlocked.Exchange(ref _outboundStreams, null);
        }

        _manager.DecrementActiveInbound();

        if (streams is not null)
        {
            _ = streams.DisposeAsync();
        }

        return true;
    }

    private void DrainChunks()
    {
        while (_chunks.Reader.TryRead(out var chunk))
        {
            chunk.Dispose();
        }
    }

    public void ReleaseCredit()
    {
        if (Volatile.Read(ref _completed) == 0)
        {
            AddPendingCredit(1);
            ScheduleCreditFlush();
        }
    }

    internal void SendCreditBestEffort(int count, CancellationToken ct) =>
        _ = SendCreditBestEffortAsync(count, ct);

    private async Task SendCreditBestEffortAsync(int count, CancellationToken ct)
    {
        try
        {
            await _manager.SendCreditAsync(Handle.StreamId, count, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Stream credit notification failed", ex);
            Abort(new DotBoxdRpcConnectionException("Stream credit notification failed.", ex));
            _manager.RemoveCompletedInbound(this);
        }
    }

    private void AddPendingCredit(int count)
    {
        while (true)
        {
            var current = Volatile.Read(ref _pendingCreditCount);
            var next = current > int.MaxValue - count ? int.MaxValue : current + count;
            if (Interlocked.CompareExchange(ref _pendingCreditCount, next, current) == current)
            {
                return;
            }
        }
    }

    private void ScheduleCreditFlush()
    {
        if (Interlocked.Exchange(ref _creditFlushScheduled, 1) == 0)
        {
            _ = FlushPendingCreditBestEffortAsync();
        }
    }

    private async Task FlushPendingCreditBestEffortAsync()
    {
        while (true)
        {
            var count = Interlocked.Exchange(ref _pendingCreditCount, 0);
            if (count > 0)
            {
                if (Volatile.Read(ref _completed) != 0)
                {
                    return;
                }

                await SendCreditBestEffortAsync(count, CancellationToken.None).ConfigureAwait(false);
            }

            if (Volatile.Read(ref _pendingCreditCount) != 0)
            {
                continue;
            }

            Interlocked.Exchange(ref _creditFlushScheduled, 0);
            if (Volatile.Read(ref _pendingCreditCount) == 0 ||
                Interlocked.Exchange(ref _creditFlushScheduled, 1) != 0)
            {
                return;
            }
        }
    }
}
