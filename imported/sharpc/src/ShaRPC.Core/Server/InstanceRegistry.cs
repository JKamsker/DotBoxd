using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ShaRPC.Core.Server;

/// <summary>
/// Default <see cref="IInstanceRegistry"/>. Backed by a single
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed on
/// <c>(serviceName, instanceId)</c>. One registry per connection.
/// </summary>
public sealed class InstanceRegistry : IInstanceRegistry
{
    internal const int DefaultMaxInstances = 1024;

    private readonly ConcurrentDictionary<(string Service, string Id), object> _entries = new();
    private readonly int _maxInstances;
    private int _count;

    public InstanceRegistry() : this(DefaultMaxInstances) { }

    public InstanceRegistry(int maxInstances)
    {
        if (maxInstances <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInstances),
                maxInstances,
                "Maximum instances must be greater than zero.");
        }

        _maxInstances = maxInstances;
    }

    /// <inheritdoc />
    public string Register(string serviceName, object instance)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));

        // Reserve a slot atomically. A plain Count check followed by an add lets several threads
        // pass the check before any of them adds, so concurrent sub-service creation could exceed
        // the configured maximum. Increment-then-check-then-rollback closes that race.
        if (Interlocked.Increment(ref _count) > _maxInstances)
        {
            Interlocked.Decrement(ref _count);
            throw new InvalidOperationException(
                $"Instance registry limit reached ({_maxInstances}). Release unused instances before registering new ones.");
        }

        var id = Guid.NewGuid().ToString("N");
        _entries[(serviceName, id)] = instance;
        return id;
    }

    /// <inheritdoc />
    public bool TryGet(string serviceName, string instanceId, out object instance)
    {
        if (_entries.TryGetValue((serviceName, instanceId), out var value))
        {
            instance = value;
            return true;
        }
        instance = null!;
        return false;
    }

    /// <inheritdoc />
    public void Release(string serviceName, string instanceId)
    {
        if (_entries.TryRemove((serviceName, instanceId), out var instance))
        {
            Interlocked.Decrement(ref _count);
            DisposeInstance(instance);
        }
    }

    /// <inheritdoc />
    public void ReleaseAll()
    {
        // TryRemove each key (Keys is a snapshot on ConcurrentDictionary) so every instance is
        // disposed exactly once and its slot freed, even if Release races in concurrently.
        foreach (var key in _entries.Keys)
        {
            if (_entries.TryRemove(key, out var instance))
            {
                Interlocked.Decrement(ref _count);
                DisposeInstance(instance);
            }
        }
    }

    /// <summary>
    /// Async teardown drain used by the connection-cleanup path. Like <see cref="ReleaseAll"/> it removes
    /// and disposes every registered instance, but it awaits <see cref="IAsyncDisposable.DisposeAsync"/>
    /// rather than blocking a pooled thread on it — avoiding the thread-pool starvation / captured-context
    /// deadlock that sync-over-async disposal causes when a user disposer suspends.
    /// </summary>
    internal async Task ReleaseAllAsync()
    {
        // TryRemove each key (Keys is a snapshot on ConcurrentDictionary) so every instance is disposed
        // exactly once and its slot freed, even if Release races in concurrently.
        foreach (var key in _entries.Keys)
        {
            if (_entries.TryRemove(key, out var instance))
            {
                Interlocked.Decrement(ref _count);
                await DisposeInstanceAsync(instance).ConfigureAwait(false);
            }
        }
    }

    // Sub-service instances are connection-scoped and owned by the registry (see IInstanceRegistry),
    // so they are disposed when released. Best-effort: a faulting dispose is reported via diagnostics
    // but never breaks teardown. IAsyncDisposable is preferred when present; the blocking wait is safe
    // because ShaRPC runs context-free (ConfigureAwait(false) throughout), so there is no captured
    // SynchronizationContext to deadlock against.
    private static void DisposeInstance(object instance)
    {
        try
        {
            switch (instance)
            {
                case IAsyncDisposable asyncDisposable:
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            ShaRPC.Core.RpcDiagnostics.Report("Sub-service instance disposal failed", ex);
        }
    }

    // Async counterpart of DisposeInstance for the teardown drain: awaits IAsyncDisposable.DisposeAsync
    // (preferred when present) instead of blocking on it, so a suspending user disposer never sync-blocks
    // a pooled thread. Same best-effort contract: a faulting dispose is reported, never rethrown.
    private static async Task DisposeInstanceAsync(object instance)
    {
        try
        {
            switch (instance)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            ShaRPC.Core.RpcDiagnostics.Report("Sub-service instance disposal failed", ex);
        }
    }
}
