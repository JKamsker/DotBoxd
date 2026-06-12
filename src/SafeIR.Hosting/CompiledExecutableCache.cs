namespace SafeIR.Hosting;

using System.Collections.Concurrent;
using SafeIR;
using SafeIR.Compiler;

internal sealed class CompiledExecutableCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<MaterializedCompiledArtifact>>> _entries =
        new(StringComparer.Ordinal);
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
        CompiledArtifactGuard.ValidateExecutableEnvelope(artifact, plan, entrypoint);
        var key = Key(artifact);
        var candidate = new Lazy<Task<MaterializedCompiledArtifact>>(
            () => _materialize(artifact, plan, entrypoint, cancellationToken).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<MaterializedCompiledArtifact>> lazy;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            lazy = _entries.GetOrAdd(key, candidate);
        }

        var status = ReferenceEquals(lazy, candidate) ? "Miss" : "Hit";

        try
        {
            var materialized = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0)
            {
                materialized.Dispose();
                throw new ObjectDisposedException(nameof(CompiledExecutableCache));
            }

            return new CompiledExecutable(WithCurrentMetadata(materialized.Artifact, artifact), status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RemoveIfCurrent(key, lazy);
            throw;
        }
        catch
        {
            RemoveIfCurrent(key, lazy);
            throw;
        }
    }

    public void Dispose()
    {
        Lazy<Task<MaterializedCompiledArtifact>>[] entries;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            entries = _entries.Values.ToArray();
            _entries.Clear();
        }

        foreach (var lazy in entries)
        {
            DisposeWhenMaterialized(lazy);
        }
    }

    private static string Key(CompiledArtifact artifact)
        => artifact.Manifest.CacheKey + "|" + artifact.AssemblyHash;

    private static CompiledArtifact WithCurrentMetadata(CompiledArtifact materialized, CompiledArtifact current)
        => materialized with
        {
            CacheStatus = current.CacheStatus,
            CacheInvalidReason = current.CacheInvalidReason
        };

    private void RemoveIfCurrent(string key, Lazy<Task<MaterializedCompiledArtifact>> lazy)
    {
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
        {
            _entries.TryRemove(key, out _);
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
}
