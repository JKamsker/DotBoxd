using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Hosting.Execution;

internal sealed class CompiledExecutableCache : IDisposable
{
    // Bounded retention: a long-lived host can materialize many unique compiled artifacts
    // (generated modules, policy/binding variants, plugin entrypoints). Each unique identity
    // occupies a distinct slot, so the cache evicts the least-recently-used materialized
    // executable once this capacity is exceeded instead of retaining all of them for the host
    // lifetime. Same-key requests still coalesce onto a single materialization.
    private const int Capacity = 64;

    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _entries = new();
    private readonly LinkedList<CacheEntry> _recency = new();
    private readonly Func<CompiledArtifact, ExecutionPlan, string, CancellationToken, ValueTask<MaterializedCompiledArtifact>> _materialize;
    private readonly object _gate = new();
    private int _disposed;

    public CompiledExecutableCache()
        : this(CompiledArtifactGuard.MaterializeExecutableAsync)
    {
    }

    internal CompiledExecutableCache(
        Func<CompiledArtifact, ExecutionPlan, string, CancellationToken, ValueTask<MaterializedCompiledArtifact>> materialize)
    {
        ArgumentNullException.ThrowIfNull(materialize);
        _materialize = materialize;
    }

    public async ValueTask<CompiledExecutable> GetAsync(
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CacheKey(artifact.Manifest.CacheKey, artifact.AssemblyHash);
        if (TryGetSameArtifactHit(key, artifact, out var sameArtifact))
        {
            return await CreateExecutableAsync(sameArtifact, artifact, "Hit", cancellationToken).ConfigureAwait(false);
        }

        CompiledArtifactGuard.ValidateExecutableEnvelope(artifact, plan, entrypoint);
        Lazy<Task<MaterializedCompiledArtifact>> lazy;
        LinkedListNode<CacheEntry>? evicted;
        bool isMiss;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            (lazy, isMiss, evicted) = TouchOrAdd(key, artifact, plan, entrypoint);
        }

        DisposeEntry(evicted);
        var status = isMiss ? "Miss" : "Hit";
        return await CreateExecutableAsync(lazy, artifact, status, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CompiledExecutable> CreateExecutableAsync(
        Lazy<Task<MaterializedCompiledArtifact>> lazy,
        CompiledArtifact current,
        string status,
        CancellationToken cancellationToken)
    {
        try
        {
            var materialized = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0)
            {
                materialized.Dispose();
                throw new ObjectDisposedException(nameof(CompiledExecutableCache));
            }

            return new CompiledExecutable(WithCurrentMetadata(materialized.Artifact, current), status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RemoveIfCurrent(new CacheKey(current.Manifest.CacheKey, current.AssemblyHash), lazy);
            throw;
        }
    }

    public void Dispose()
    {
        LinkedListNode<CacheEntry>[] entries;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            entries = new LinkedListNode<CacheEntry>[_recency.Count];
            var index = 0;
            for (var node = _recency.First; node is not null; node = node.Next)
            {
                entries[index++] = node;
            }

            _entries.Clear();
            _recency.Clear();
        }

        foreach (var node in entries)
        {
            DisposeWhenMaterialized(node.Value.Lazy);
        }
    }

    private (Lazy<Task<MaterializedCompiledArtifact>> Lazy, bool IsMiss, LinkedListNode<CacheEntry>? Evicted) TouchOrAdd(
        CacheKey key,
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            _recency.Remove(existing);
            _recency.AddLast(existing);
            existing.Value = existing.Value with { ValidatedArtifact = artifact };
            return (existing.Value.Lazy, false, null);
        }

        var candidate = new Lazy<Task<MaterializedCompiledArtifact>>(
            () => _materialize(artifact, plan, entrypoint, CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var node = _recency.AddLast(new CacheEntry(key, candidate, artifact));
        _entries[key] = node;

        var evicted = _entries.Count > Capacity ? EvictOldest() : null;
        return (candidate, true, evicted);
    }

    private LinkedListNode<CacheEntry>? EvictOldest()
    {
        var oldest = _recency.First;
        if (oldest is null)
        {
            return null;
        }

        _recency.Remove(oldest);
        _entries.Remove(oldest.Value.Key);
        return oldest;
    }

    private static CompiledArtifact WithCurrentMetadata(CompiledArtifact materialized, CompiledArtifact current)
    {
        if (materialized.CacheStatus == current.CacheStatus &&
            string.Equals(materialized.CacheInvalidReason, current.CacheInvalidReason, StringComparison.Ordinal))
        {
            return materialized;
        }

        return materialized with
        {
            CacheStatus = current.CacheStatus,
            CacheInvalidReason = current.CacheInvalidReason
        };
    }

    private bool TryGetSameArtifactHit(
        CacheKey key,
        CompiledArtifact artifact,
        out Lazy<Task<MaterializedCompiledArtifact>> lazy)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_entries.TryGetValue(key, out var existing) &&
                ReferenceEquals(existing.Value.ValidatedArtifact, artifact))
            {
                _recency.Remove(existing);
                _recency.AddLast(existing);
                lazy = existing.Value.Lazy;
                return true;
            }
        }

        lazy = null!;
        return false;
    }

    private void RemoveIfCurrent(CacheKey key, Lazy<Task<MaterializedCompiledArtifact>> lazy)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Lazy, lazy))
            {
                _recency.Remove(current);
                _entries.Remove(key);
            }
        }
    }

    private static void DisposeEntry(LinkedListNode<CacheEntry>? node)
    {
        if (node is not null)
        {
            DisposeWhenMaterialized(node.Value.Lazy);
        }
    }

    private static void DisposeWhenMaterialized(Lazy<Task<MaterializedCompiledArtifact>> lazy)
    {
        if (!lazy.IsValueCreated)
        {
            return;
        }

        var task = lazy.Value;
        if (task.IsCompletedSuccessfully)
        {
            task.Result.Dispose();
            return;
        }

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Status == TaskStatus.RanToCompletion)
                {
                    completed.Result.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private readonly record struct CacheKey(string ManifestCacheKey, string AssemblyHash);

    private readonly record struct CacheEntry(
        CacheKey Key,
        Lazy<Task<MaterializedCompiledArtifact>> Lazy,
        CompiledArtifact ValidatedArtifact);
}
