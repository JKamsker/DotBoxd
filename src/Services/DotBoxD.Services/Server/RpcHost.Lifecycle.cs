using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Server;

public sealed partial class RpcHost
{
    public async Task StartAsync(CancellationToken ct = default)
    {
        var cts = BeginStart(ct);
        try
        {
            await _listener.StartAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            HandleStartFailure(cts, ex);
            throw;
        }

        var onListenerStarted = _onListenerStartedForTest;
        if (onListenerStarted is not null)
        {
            await onListenerStarted().ConfigureAwait(false);
        }

        var recovery = CompleteStart(cts);
        if (recovery is null)
        {
            return;
        }

        await RecoverStartedListenerAsync(recovery.Value).ConfigureAwait(false);
    }

    private CancellationTokenSource BeginStart(CancellationToken ct)
    {
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(RpcHost));
            }

            if (_cts is not null || _starting)
            {
                throw new InvalidOperationException("Host is already running.");
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            _starting = true;
            _listenerStopped = 0;
            return cts;
        }
    }

    private void HandleStartFailure(CancellationTokenSource cts, Exception ex)
    {
        bool disposed;
        lock (_lifecycleLock)
        {
            disposed = Volatile.Read(ref _disposed) != 0;
            if (ReferenceEquals(_cts, cts) && _stopTask is null)
            {
                _cts = null;
                cts.Dispose();
            }

            _starting = false;
        }

        if (disposed && ex is OperationCanceledException)
        {
            throw new ObjectDisposedException(nameof(RpcHost));
        }
    }

    private StartRecovery? CompleteStart(CancellationTokenSource cts)
    {
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                var ownsCts = ReferenceEquals(_cts, cts);
                var disposeCts = ownsCts && _stopTask is null;
                if (ownsCts)
                {
                    _cts = null;
                    _stopTask = null;
                }

                return new StartRecovery(
                    Cts: cts,
                    DisposeCts: disposeCts,
                    Failure: new ObjectDisposedException(nameof(RpcHost)));
            }

            if (!ReferenceEquals(_cts, cts) || _stopTask is not null)
            {
                return new StartRecovery(Cts: cts, DisposeCts: false, Failure: null);
            }

            if (cts.IsCancellationRequested)
            {
                _cts = null;
                return new StartRecovery(
                    Cts: cts,
                    DisposeCts: true,
                    Failure: new InvalidOperationException("Host start was stopped before it completed."));
            }

            _starting = false;
            _acceptTask = _acceptLoop.RunAsync(cts.Token);
            return null;
        }
    }

    private async Task RecoverStartedListenerAsync(StartRecovery recovery)
    {
        try
        {
            if (Interlocked.Exchange(ref _listenerStopped, 1) == 0)
            {
                await _listener.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception stopEx)
        {
            RpcDiagnostics.Report("Listener stop during start recovery failed", stopEx);
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _starting = false;
            }

            if (recovery.DisposeCts)
            {
                recovery.Cts.Dispose();
            }
        }

        if (recovery.Failure is not null)
        {
            throw recovery.Failure;
        }

        throw new InvalidOperationException("Host start was stopped before it completed.");
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_lifecycleLock)
        {
            if (_cts is null)
            {
                return Task.CompletedTask;
            }
            return _stopTask ??= StopCoreAsync(_cts, _acceptTask, ct);
        }
    }
    private async Task StopCoreAsync(CancellationTokenSource cts, Task? acceptTask, CancellationToken ct)
    {
        var completed = false;
        var cancellationStarted = false;
        var stopListenerBeforeCancel = acceptTask is not null && ct.IsCancellationRequested;
        try
        {
            if (stopListenerBeforeCancel)
            {
                await Task.Yield();
                await StopListenerOnceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            TryCancel(cts);
            cancellationStarted = true;
            if (acceptTask is not null)
            {
                await ObserveAcceptShutdownAsync(acceptTask).ConfigureAwait(false);
            }
            await _acceptLoop.DrainInFlightAsync().ConfigureAwait(false);
            if (!stopListenerBeforeCancel)
            {
                await StopListenerOnceAsync(ct).ConfigureAwait(false);
            }
            await _peers.CloseAllAsync().ConfigureAwait(false);
            await _peers.AwaitCleanupAsync().ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (cancellationStarted)
            {
                DisposeCts(cts);
            }
            CompleteStop(cts, completed);
        }
    }
    private static void TryCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CTS was disposed by a prior failed stop attempt.
        }
    }

    private static void DisposeCts(CancellationTokenSource cts)
    {
        try
        {
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a prior failed stop attempt.
        }
    }

    private static async Task ObserveAcceptShutdownAsync(Task acceptTask)
    {
        try
        {
            await acceptTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Accept loop fault during shutdown", ex);
        }
    }

    private async Task StopListenerOnceAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _listenerStopped, 1) != 0)
        {
            return;
        }

        try
        {
            await _listener.StopAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Volatile.Write(ref _listenerStopped, 0);
            throw;
        }
    }

    private void CompleteStop(CancellationTokenSource cts, bool completed)
    {
        lock (_lifecycleLock)
        {
            if (!ReferenceEquals(_cts, cts))
            {
                return;
            }

            _stopTask = null;
            if (completed)
            {
                _cts = null;
                _acceptTask = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _peers.CloseAllAsync().ConfigureAwait(false);
                await _peers.AwaitCleanupAsync().ConfigureAwait(false);
            }
            finally
            {
                await _listener.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private readonly record struct StartRecovery(
        CancellationTokenSource Cts,
        bool DisposeCts,
        Exception? Failure);
}
