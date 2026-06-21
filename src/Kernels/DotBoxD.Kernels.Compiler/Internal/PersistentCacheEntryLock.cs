namespace DotBoxD.Kernels.Compiler.Internal;

internal sealed class PersistentCacheEntryLock : IAsyncDisposable
{
    private const int RetryDelayMilliseconds = 10;

    private readonly FileStream _stream;

    private PersistentCacheEntryLock(FileStream stream) => _stream = stream;

    public static async ValueTask<PersistentCacheEntryLock> AcquireAsync(
        string rootDirectory,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var path = LockPath(rootDirectory, cacheKey);
        var lockDirectory = Path.GetDirectoryName(path)!;
        PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(rootDirectory, lockDirectory);
        Directory.CreateDirectory(lockDirectory);
        PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(rootDirectory, lockDirectory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                return new PersistentCacheEntryLock(stream);
            }
            catch (IOException)
            {
                await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string LockPath(string rootDirectory, string cacheKey)
        => Path.Combine(rootDirectory, ".locks", cacheKey[..2], cacheKey[2..4], cacheKey + ".lock");
}
