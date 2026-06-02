using System.Threading.Channels;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcPeerInboundRequestQueue
{
    private readonly Channel<RpcPeerInboundRequest> _queue;
    private readonly Func<RpcPeerInboundRequest, Task> _processAsync;
    private readonly Action<RpcPeerInboundRequest> _release;
    private readonly bool _dropIncomingWhenFull;
    private CancellationTokenSource? _cts;
    private Task? _dispatchWorker;

    public RpcPeerInboundRequestQueue(
        int capacity,
        ShaRpcQueueFullMode mode,
        Func<RpcPeerInboundRequest, Task> processAsync,
        Action<RpcPeerInboundRequest> release)
    {
        _processAsync = processAsync;
        _release = release;
        _dropIncomingWhenFull = mode == ShaRpcQueueFullMode.DropIncoming;
        _queue = Channel.CreateBounded<RpcPeerInboundRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public void Start(CancellationToken loopCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        _dispatchWorker = Task.Run(() => DispatchAsync(_cts.Token));
    }

    public async ValueTask<bool> EnqueueAsync(RpcPeerInboundRequest inbound, CancellationToken ct)
    {
        if (_dropIncomingWhenFull)
        {
            if (_queue.Writer.TryWrite(inbound))
            {
                return true;
            }

            // Release the request resources but leave frame disposal to the read loop, which
            // disposes on the false return. Disposing here too would double-return the pooled
            // buffer (benign only while Payload.Dispose stays idempotent).
            _release(inbound);
            return false;
        }

        try
        {
            await _queue.Writer.WriteAsync(inbound, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _release(inbound);
            return false;
        }
        catch (ChannelClosedException)
        {
            _release(inbound);
            return false;
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _queue.Writer.TryComplete();

        if (_dispatchWorker is not null)
        {
            await ObserveShutdownAsync(_dispatchWorker).ConfigureAwait(false);
        }

        Drain();
        _cts?.Dispose();
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var inbound))
                {
                    try
                    {
                        await _processAsync(inbound).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        RpcDiagnostics.Report("Inbound request dispatch failed", ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during peer shutdown.
        }
    }

    private void Drain()
    {
        while (_queue.Reader.TryRead(out var inbound))
        {
            inbound.Frame.Dispose();
            _release(inbound);
        }
    }

    private static async Task ObserveShutdownAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Request dispatch observes its own failures.
        }
    }
}
