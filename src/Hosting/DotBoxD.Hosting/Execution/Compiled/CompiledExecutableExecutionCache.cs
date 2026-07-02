namespace DotBoxD.Hosting.Execution.Compiled;

internal sealed class CompiledExecutableExecutionCache
{
    private const int Capacity = 64;

    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recency = new();
    private readonly object _gate = new();

    public async ValueTask<CompiledExecutable> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CacheKey(plan.PlanHash, entrypoint);
        CacheLookup lookup;
        lock (_gate)
        {
            lookup = TouchOrAdd(key, materialize);
        }

        if (lookup.Completed is { } completed)
        {
            return completed with { MaterializationStatus = "Hit" };
        }

        var lazy = lookup.Lazy!;
        try
        {
            var executable = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            MarkCompleted(key, lazy, executable);
            return lookup.IsMiss ? executable : executable with { MaterializationStatus = "Hit" };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RemoveIfCurrent(key, lazy);
            throw;
        }
    }

    private CacheLookup TouchOrAdd(
        CacheKey key,
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            _recency.Remove(existing);
            _recency.AddLast(existing);
            return existing.Value.Completed is { } completed
                ? new CacheLookup(completed, null, false)
                : new CacheLookup(null, existing.Value.Executable, false);
        }

        var candidate = new Lazy<Task<CompiledExecutable>>(
            () => materialize(CancellationToken.None).AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var node = _recency.AddLast(new Entry(key, candidate));
        _entries[key] = node;
        if (_entries.Count > Capacity)
        {
            var oldest = _recency.First!;
            _recency.Remove(oldest);
            _entries.Remove(oldest.Value.Key);
        }

        return new CacheLookup(null, candidate, true);
    }

    private void MarkCompleted(CacheKey key, Lazy<Task<CompiledExecutable>> lazy, CompiledExecutable executable)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Executable, lazy))
            {
                current.Value.Completed = executable;
            }
        }
    }

    private void RemoveIfCurrent(CacheKey key, Lazy<Task<CompiledExecutable>> lazy)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value.Executable, lazy))
            {
                _recency.Remove(current);
                _entries.Remove(key);
            }
        }
    }

    private readonly record struct CacheLookup(
        CompiledExecutable? Completed,
        Lazy<Task<CompiledExecutable>>? Lazy,
        bool IsMiss);

    private readonly record struct CacheKey(string PlanHash, string Entrypoint);

    private sealed class Entry(CacheKey key, Lazy<Task<CompiledExecutable>> executable)
    {
        public CacheKey Key { get; } = key;
        public Lazy<Task<CompiledExecutable>> Executable { get; } = executable;
        public CompiledExecutable? Completed { get; set; }
    }
}
