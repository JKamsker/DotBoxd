using DotBoxd.Kernels.Compiler.Internal;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression test for PAL-0010: the persistent compiled artifact cache opens a cross-process
/// file lock under <c>.locks/&lt;prefix&gt;/&lt;prefix&gt;/&lt;cacheKey&gt;.lock</c> with
/// <see cref="FileMode.OpenOrCreate"/> for every unique cache key, and never removes the lock
/// file after releasing it. A service that compiles/probes many unique plans therefore leaks one
/// stale <c>.lock</c> file per historical cache key.
/// </summary>
public sealed class Fix_PAL_0010_Tests
{
    private const int UniqueKeyCount = 256;

    [Fact]
    public async Task Releasing_lock_for_many_unique_keys_does_not_retain_one_lock_file_per_key()
    {
        using var root = TempRoot.Create();

        for (var i = 0; i < UniqueKeyCount; i++) {
            var key = SyntheticCacheKey(i);
            await using var entryLock = await PersistentCacheEntryLock.AcquireAsync(
                root.Path,
                key,
                CancellationToken.None);
            // Lock is released by the await-using dispose at the end of each iteration.
        }

        var retainedLockFiles = CountLockFiles(root.Path);

        // The correct behavior is that released lock files are not retained unbounded.
        // Today DisposeAsync only closes the FileStream and leaves the file behind, so the
        // ".locks" tree accumulates exactly one file per unique cache key (currently red).
        Assert.True(
            retainedLockFiles < UniqueKeyCount,
            $"Expected released lock files to be cleaned up, but the .locks tree retained " +
            $"{retainedLockFiles} files for {UniqueKeyCount} unique cache keys.");
    }

    [Fact]
    public async Task Same_key_lock_serializes_concurrent_acquirers()
    {
        using var root = TempRoot.Create();
        var key = SyntheticCacheKey(0);

        var concurrent = 0;
        var maxConcurrent = 0;
        var gate = new object();

        async Task ContendAsync()
        {
            await using var entryLock = await PersistentCacheEntryLock.AcquireAsync(
                root.Path,
                key,
                CancellationToken.None);

            lock (gate) {
                concurrent++;
                if (concurrent > maxConcurrent) {
                    maxConcurrent = concurrent;
                }
            }

            await Task.Delay(5);

            lock (gate) {
                concurrent--;
            }
        }

        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(ContendAsync)).ToArray();
        await Task.WhenAll(workers);

        // Same-key mutual exclusion must continue to hold regardless of how lock files are pruned.
        Assert.Equal(1, maxConcurrent);
    }

    private static string SyntheticCacheKey(int index)
        => index.ToString("x2") + new string('a', 62);

    private static int CountLockFiles(string root)
    {
        var locksDirectory = Path.Combine(root, ".locks");
        if (!Directory.Exists(locksDirectory)) {
            return 0;
        }

        return Directory
            .EnumerateFiles(locksDirectory, "*.lock", SearchOption.AllDirectories)
            .Count();
    }

    private sealed class TempRoot : IDisposable
    {
        private TempRoot(string path) => Path = path;

        public string Path { get; }

        public static TempRoot Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-pal0010-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempRoot(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
