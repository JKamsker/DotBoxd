using System.Threading.Channels;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcPeerInboundRequestQueue
{
    private readonly Channel<RpcPeerInboundRequest> _dispatchQueue;
    private readonly Channel<RpcPeerInboundRequest>? _intakeQueue;
    private readonly Func<RpcPeerInboundRequest, Task> _processAsync;
    private readonly Action<RpcPeerInboundRequest> _release;
    private readonly bool _dropIncomingWhenFull;
    private CancellationTokenSource? _cts;
    private Task? _admissionWorker;
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
        _dispatchQueue = Channel.CreateBounded<RpcPeerInboundRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        if (!_dropIncomingWhenFull)
        {
            _intakeQueue = Channel.CreateUnbounded<RpcPeerInboundRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });
        }
    }

    public void Start(CancellationToken loopCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        if (_intakeQueue is not null)
        {
            _admissionWorker = Task.Run(() => AdmitAsync(_cts.Token));
        }

        _dispatchWorker = Task.Run(() => DispatchAsync(_cts.Token));
    }

    public bool TryEnqueue(RpcPeerInboundRequest inbound)
    {
        if (_dropIncomingWhenFull)
        {
            if (_dispatchQueue.Writer.TryWrite(inbound))
            {
                return true;
            }

            _release(inbound);
            return false;
        }

        if (_intakeQueue!.Writer.TryWrite(inbound))
        {
            return true;
        }

        _release(inbound);
        return false;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _intakeQueue?.Writer.TryComplete();
        _dispatchQueue.Writer.TryComplete();

        if (_admissionWorker is not null)
        {
            await ObserveShutdownAsync(_admissionWorker).ConfigureAwait(false);
        }

        if (_dispatchWorker is not null)
        {
            await ObserveShutdownAsync(_dispatchWorker).ConfigureAwait(false);
        }

        Drain(_intakeQueue);
        Drain(_dispatchQueue);
        _cts?.Dispose();
    }

    private async Task AdmitAsync(CancellationToken ct)
    {
        try
        {
            while (await _intakeQueue!.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_intakeQueue.Reader.TryRead(out var inbound))
                {
                    await WriteToDispatchQueueAsync(inbound, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during peer shutdown.
        }
        finally
        {
            _dispatchQueue.Writer.TryComplete();
        }
    }

    private async Task WriteToDispatchQueueAsync(RpcPeerInboundRequest inbound, CancellationToken ct)
    {
        try
        {
            await _dispatchQueue.Writer.WriteAsync(inbound, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            inbound.Frame.Dispose();
            _release(inbound);
        }
        catch (ChannelClosedException)
        {
            inbound.Frame.Dispose();
            _release(inbound);
        }
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        try
        {
            while (await _dispatchQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_dispatchQueue.Reader.TryRead(out var inbound))
                {
                    await _processAsync(inbound).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during peer shutdown.
        }
    }

    private void Drain(Channel<RpcPeerInboundRequest>? channel)
    {
        if (channel is null)
        {
            return;
        }

        while (channel.Reader.TryRead(out var inbound))
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
