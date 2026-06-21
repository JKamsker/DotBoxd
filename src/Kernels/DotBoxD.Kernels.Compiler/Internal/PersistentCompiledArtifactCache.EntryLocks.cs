namespace DotBoxD.Kernels.Compiler;

public sealed partial class PersistentCompiledArtifactCache
{
    private async ValueTask<T> WithEntryLockAsync<T>(
        string cacheKey,
        Func<ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        using var entryLock = await AcquireEntryLockAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        var fileLock = await PersistentCacheEntryLock
            .AcquireAsync(_rootDirectory, cacheKey, cancellationToken)
            .ConfigureAwait(false);
        await using (fileLock.ConfigureAwait(false))
        {
            return await action().ConfigureAwait(false);
        }
    }

    private async ValueTask WithEntryLockAsync(
        string cacheKey,
        Func<ValueTask> action,
        CancellationToken cancellationToken)
    {
        using var entryLock = await AcquireEntryLockAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        var fileLock = await PersistentCacheEntryLock
            .AcquireAsync(_rootDirectory, cacheKey, cancellationToken)
            .ConfigureAwait(false);
        await using (fileLock.ConfigureAwait(false))
        {
            await action().ConfigureAwait(false);
        }
    }

    private async ValueTask<EntryLockLease> AcquireEntryLockAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var entryLock = RetainEntryLock(cacheKey);
        var acquired = false;
        try
        {
            await entryLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return new EntryLockLease(this, cacheKey, entryLock);
        }
        finally
        {
            if (!acquired)
            {
                ReleaseEntryLock(cacheKey, entryLock, releaseSemaphore: false);
            }
        }
    }

    private EntryLock RetainEntryLock(string cacheKey)
    {
        while (true)
        {
            var entryLock = _entryLocks.GetOrAdd(cacheKey, static _ => new EntryLock());
            if (entryLock.TryRetain())
            {
                return entryLock;
            }

            Thread.Yield();
        }
    }

    private void ReleaseEntryLock(string cacheKey, EntryLock entryLock, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entryLock.Semaphore.Release();
        }

        if (!entryLock.Release())
        {
            return;
        }

        var removed = ((ICollection<KeyValuePair<string, EntryLock>>)_entryLocks)
            .Remove(new KeyValuePair<string, EntryLock>(cacheKey, entryLock));
        if (removed)
        {
            entryLock.Dispose();
        }
    }

    private sealed class EntryLock : IDisposable
    {
        private readonly object _gate = new();
        private int _referenceCount;
        private bool _removed;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryRetain()
        {
            lock (_gate)
            {
                if (_removed)
                {
                    return false;
                }

                _referenceCount++;
                return true;
            }
        }

        public bool Release()
        {
            lock (_gate)
            {
                _referenceCount--;
                if (_referenceCount != 0)
                {
                    return false;
                }

                _removed = true;
                return true;
            }
        }

        public void Dispose()
            => Semaphore.Dispose();
    }

    private sealed class EntryLockLease : IDisposable
    {
        private readonly PersistentCompiledArtifactCache _owner;
        private readonly string _cacheKey;
        private readonly EntryLock _entryLock;
        private int _disposed;

        public EntryLockLease(
            PersistentCompiledArtifactCache owner,
            string cacheKey,
            EntryLock entryLock)
        {
            _owner = owner;
            _cacheKey = cacheKey;
            _entryLock = entryLock;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.ReleaseEntryLock(_cacheKey, _entryLock, releaseSemaphore: true);
            }
        }
    }
}
