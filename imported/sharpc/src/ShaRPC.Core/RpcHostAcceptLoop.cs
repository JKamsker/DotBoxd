using System.Collections.Concurrent;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcHostAcceptLoop
{
    private static readonly TimeSpan AcceptErrorBackoff = TimeSpan.FromMilliseconds(50);

    private readonly IServerTransport _listener;
    private readonly Func<IRpcChannel, Task> _addPeerAsync;
    private readonly Action<Exception> _acceptError;
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    public RpcHostAcceptLoop(
        IServerTransport listener,
        Func<IRpcChannel, Task> addPeerAsync,
        Action<Exception> acceptError)
    {
        _listener = listener;
        _addPeerAsync = addPeerAsync;
        _acceptError = acceptError;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IRpcChannel connection;
            try
            {
                connection = await _listener.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _acceptError(ex);
                if (!await DelayAfterErrorAsync(ct).ConfigureAwait(false))
                {
                    break;
                }

                continue;
            }

            TrackHandoff(connection);
        }
    }

    /// <summary>
    /// Awaits every peer hand-off the loop has started. Call after the loop task completes so a
    /// connection accepted just before shutdown finishes registering (and is then disposed by the
    /// host's peer drain) instead of starting a peer the host never tears down.
    /// </summary>
    public async Task DrainInFlightAsync()
    {
        var tasks = _inFlight.Keys.ToArray();
        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Each hand-off observes its own failure; we only need them quiesced before peer drain.
        }
    }

    private void TrackHandoff(IRpcChannel connection)
    {
        var handoff = Task.Run(async () =>
        {
            try
            {
                await _addPeerAsync(connection).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _acceptError(ex);
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        });

        // Register before attaching the self-removal continuation so a hand-off that finishes
        // before TryAdd still gets removed by the continuation firing on the completed task.
        _inFlight.TryAdd(handoff, 0);
        _ = handoff.ContinueWith(
            static (task, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(task, out _),
            _inFlight,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task<bool> DelayAfterErrorAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(AcceptErrorBackoff, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
