using System.Threading.Channels;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Peer.Inbound;

/// <summary>
/// Outcome of attempting to enqueue an inbound request for dispatch.
/// </summary>
internal enum InboundEnqueueResult
{
    /// <summary>The request was queued (or dispatched) and the queue now owns the frame.</summary>
    Accepted,

    /// <summary>The queue was full in DropIncoming mode; the caller should notify the peer.</summary>
    Dropped,

    /// <summary>The peer is shutting down (cancelled or channel closed); no notification needed.</summary>
    ShuttingDown,
}

internal sealed class RpcPeerInboundRequestQueue
{
    private readonly Channel<RpcPeerInboundRequest> _queue;
    private readonly Func<RpcPeerInboundRequest, Task> _processAsync;
    private readonly Action<RpcPeerInboundRequest> _release;
    private readonly bool _dropIncomingWhenFull;
    private readonly bool _dispatchSerially;
    private readonly SemaphoreSlim? _slots;
    private readonly RpcPeerInboundByteBudget _byteBudget;
    private readonly RpcPeerInboundInFlightTracker _inFlight = new();
    private CancellationTokenSource? _cts;
    private Task? _dispatchWorker;

    public RpcPeerInboundRequestQueue(
        int capacity,
        QueueFullMode mode,
        int maxConcurrency,
        long? maxInboundBytes,
        Func<RpcPeerInboundRequest, Task> processAsync,
        Action<RpcPeerInboundRequest> release)
    {
        _processAsync = processAsync;
        _release = release;
        _dropIncomingWhenFull = mode == QueueFullMode.DropIncoming;
        _dispatchSerially = maxConcurrency == 1;
        // long.MaxValue == byte bound disabled (count-only). Otherwise total in-flight inbound frame
        // bytes are capped at maxInboundBytes, independent of the capacity (count) bound.
        _byteBudget = new RpcPeerInboundByteBudget(maxInboundBytes);
        // maxConcurrency == 1 keeps dispatch strictly serial; > 1 admits that many concurrent
        // dispatches. Total in-flight inbound work is bounded by capacity + maxConcurrency.
        _slots = _dispatchSerially ? null : new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _queue = Channel.CreateBounded<RpcPeerInboundRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public void Start(CancellationToken loopCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        _dispatchWorker = Task.Run(() => DispatchAsync(_cts.Token));
    }

    public async ValueTask<InboundEnqueueResult> EnqueueAsync(RpcPeerInboundRequest inbound, CancellationToken ct)
    {
        var bytes = inbound.Frame.Length;

        if (_dropIncomingWhenFull)
        {
            // Drop when over the byte budget or when the count queue is full. Release the request
            // resources but leave frame disposal to the read loop, which disposes on the Dropped
            // return. Disposing here too would double-return the pooled buffer (benign only while
            // Payload.Dispose stays idempotent).
            if (_byteBudget.TryAdmit(bytes))
            {
                if (_queue.Writer.TryWrite(inbound))
                {
                    return InboundEnqueueResult.Accepted;
                }

                _byteBudget.Release(bytes);
            }

            _release(inbound);
            return InboundEnqueueResult.Dropped;
        }

        try
        {
            // Wait for byte-budget headroom before committing the frame to the queue, so peak
            // in-flight inbound memory stays bounded regardless of how many large frames a peer sends.
            await _byteBudget.AdmitAsync(bytes, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // No ReleaseBytes here: AdmitBytesAsync admits atomically — it increments _inFlightBytes only
            // immediately before returning, with no await in between — and its sole throw point is the
            // wait that runs *before* admitting. A cancelled admit therefore reserved nothing to release.
            _release(inbound);
            return InboundEnqueueResult.ShuttingDown;
        }

        var committed = false;
        try
        {
            if (_queue.Writer.TryWrite(inbound))
            {
                committed = true;
                return InboundEnqueueResult.Accepted;
            }

            await _queue.Writer.WriteAsync(inbound, ct).ConfigureAwait(false);
            committed = true;
            return InboundEnqueueResult.Accepted;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return InboundEnqueueResult.ShuttingDown;
        }
        catch (ChannelClosedException)
        {
            return InboundEnqueueResult.ShuttingDown;
        }
        finally
        {
            // Any path that did not hand the frame to the queue — cancellation, a closed channel, or an
            // unexpected WriteAsync failure that propagates — must return the admitted bytes and release
            // the request. Otherwise the byte budget leaks and the peer eventually stops admitting
            // inbound work even though nothing is in flight. On the return paths the read loop disposes
            // the frame (EnqueueAsync returned false); on a propagating throw the read loop's finally
            // disposes it.
            if (!committed)
            {
                _byteBudget.Release(bytes);
                _release(inbound);
            }
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

        await _inFlight.WaitAsync().ConfigureAwait(false);

        Drain();
        _slots?.Dispose();
        _cts?.Dispose();
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        if (_dispatchSerially)
        {
            await DispatchSerialAsync(ct).ConfigureAwait(false);
            return;
        }

        var slots = _slots ?? throw new InvalidOperationException("parallel dispatch requires a slot semaphore");
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                // Acquire a dispatch slot BEFORE pulling the item off the channel, so a blocked
                // dispatcher does not let the worker drain extra items into limbo: at most
                // maxConcurrency items are removed from the channel beyond what is dispatching,
                // keeping read-side backpressure at exactly capacity + maxConcurrency (and
                // identical to inline serial dispatch when maxConcurrency == 1).
                await slots.WaitAsync(ct).ConfigureAwait(false);
                if (_queue.Reader.TryRead(out var inbound))
                {
                    StartProcessing(inbound, slots);
                }
                else
                {
                    // Writer completed with no item left for this slot; hand it back.
                    slots.Release();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during peer shutdown.
        }
    }

    private async Task DispatchSerialAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var inbound))
                {
                    _inFlight.Add();
                    var bytes = inbound.Frame.Length;
                    try
                    {
                        await _processAsync(inbound).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        RpcDiagnostics.Report("Inbound request dispatch failed", ex);
                    }
                    finally
                    {
                        _byteBudget.Release(bytes);
                        _inFlight.Complete();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during peer shutdown.
        }
    }

    private void StartProcessing(RpcPeerInboundRequest inbound, SemaphoreSlim slots)
    {
        _inFlight.Add();
        _ = ProcessOneAsync(inbound, slots);
    }

    private async Task ProcessOneAsync(RpcPeerInboundRequest inbound, SemaphoreSlim slots)
    {
        // Capture before dispatch: _processAsync disposes the frame, and the byte budget must be
        // released exactly once when the frame leaves the queue.
        var bytes = inbound.Frame.Length;
        try
        {
            await _processAsync(inbound).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Inbound request dispatch failed", ex);
        }
        finally
        {
            _byteBudget.Release(bytes);
            try
            {
                slots.Release();
            }
            catch (ObjectDisposedException)
            {
                // StopAsync disposed the slot semaphore after the dispatch worker stopped.
            }

            _inFlight.Complete();
        }
    }

    private void Drain()
    {
        while (_queue.Reader.TryRead(out var inbound))
        {
            _byteBudget.Release(inbound.Frame.Length);
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
