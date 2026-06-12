namespace SafeIR.Compiler;

using System.Collections.Concurrent;
using System.Text.Json;
using SafeIR;
using SafeIR.Verifier;

public sealed class PersistentCompiledArtifactCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _entryLocks = new(StringComparer.Ordinal);

    public PersistentCompiledArtifactCache(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
        PersistentCompiledArtifactCacheRootGuard.Validate(_rootDirectory);
    }

    public bool EntryExists(string cacheKey)
    {
        var entryPath = EntryPath(cacheKey);
        PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(_rootDirectory, entryPath);
        return Directory.Exists(entryPath);
    }

    public async ValueTask<CompiledCacheLookup> TryReadAsync(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        IGeneratedAssemblyVerifier verifier,
        VerificationPolicy policy,
        CancellationToken cancellationToken)
    {
        PersistentCompiledArtifactCacheValidator.ValidateCacheKey(cacheKey);
        return await WithEntryLockAsync(
                cacheKey,
                () => TryReadCoreAsync(cacheKey, plan, entrypoint, verifier, policy, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        byte[] assemblyBytes,
        ArtifactManifest manifest,
        VerificationResult verification,
        VerificationPolicy policy,
        CancellationToken cancellationToken)
    {
        PersistentCompiledArtifactCacheValidator.ValidateCacheKey(cacheKey);
        await WithEntryLockAsync(
                cacheKey,
                () => WriteCoreAsync(cacheKey, plan, entrypoint, assemblyBytes, manifest, verification, policy, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public string EntryPath(string cacheKey)
    {
        PersistentCompiledArtifactCacheValidator.ValidateCacheKey(cacheKey);
        return Path.Combine(_rootDirectory, cacheKey[..2], cacheKey[2..4], cacheKey);
    }

    private async ValueTask<CompiledCacheLookup> TryReadCoreAsync(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        IGeneratedAssemblyVerifier verifier,
        VerificationPolicy policy,
        CancellationToken cancellationToken)
    {
        var entryPath = EntryPath(cacheKey);
        try
        {
            PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(_rootDirectory, entryPath);
        }
        catch (SandboxRuntimeException ex)
        {
            return new CompiledCacheLookup(CompiledCacheStatus.Invalid, null, CacheInvalidReason(ex));
        }

        if (!Directory.Exists(entryPath))
        {
            return new CompiledCacheLookup(CompiledCacheStatus.Miss, null);
        }

        try
        {
            var manifest = await ReadJsonAsync<ArtifactManifest>(Path.Combine(entryPath, "manifest.json"), cancellationToken)
                .ConfigureAwait(false);
            PersistentCompiledArtifactCacheValidator.ValidateManifest(cacheKey, plan, entrypoint, manifest, policy);
            var cachedVerification = await ReadJsonAsync<VerificationResult>(
                    Path.Combine(entryPath, "verification.json"),
                    cancellationToken)
                .ConfigureAwait(false);
            PersistentCompiledArtifactCacheValidator.ValidateVerification(manifest, cachedVerification, policy);
            var assemblyBytes = await File.ReadAllBytesAsync(Path.Combine(entryPath, "module.dll"), cancellationToken)
                .ConfigureAwait(false);
            var verification = await verifier
                .VerifyAsync(
                    assemblyBytes,
                    manifest,
                    policy.WithExpectedManifest(VerificationManifestIdentity.FromManifest(manifest)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!verification.Succeeded)
            {
                throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.VerifierFailure, "cached artifact failed verification"));
            }

            return new CompiledCacheLookup(CompiledCacheStatus.Hit, new CompiledArtifact(
                assemblyBytes,
                verification.AssemblyHash,
                manifest,
                verification,
                (_, _) => throw new InvalidOperationException("cached artifact entrypoint is loaded by the compiler"),
                CompiledRuntimeFormKind.LoadedAssembly,
                CompiledCacheStatus.Hit));
        }
        catch (Exception ex) when (ex is IOException or JsonException or SandboxRuntimeException or UnauthorizedAccessException or ArgumentException)
        {
            Quarantine(entryPath);
            return new CompiledCacheLookup(CompiledCacheStatus.Invalid, null, CacheInvalidReason(ex));
        }
    }

    private async ValueTask WriteCoreAsync(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        byte[] assemblyBytes,
        ArtifactManifest manifest,
        VerificationResult verification,
        VerificationPolicy policy,
        CancellationToken cancellationToken)
    {
        PersistentCompiledArtifactCacheValidator.ValidateManifest(cacheKey, plan, entrypoint, manifest, policy);
        PersistentCompiledArtifactCacheValidator.ValidateVerification(manifest, verification, policy);

        var finalPath = EntryPath(cacheKey);
        PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(_rootDirectory, finalPath);
        var tempPath = Path.Combine(_rootDirectory, ".tmp-" + Guid.NewGuid().ToString("N"));
        var previousPath = Path.Combine(_rootDirectory, ".old-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            await WriteBytesAsync(Path.Combine(tempPath, "module.dll"), assemblyBytes, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(Path.Combine(tempPath, "manifest.json"), manifest, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(Path.Combine(tempPath, "verification.json"), verification, cancellationToken).ConfigureAwait(false);
            PersistentCompiledArtifactCachePublisher.ValidateEntryShape(tempPath);

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(_rootDirectory, finalPath);

            var movedPrevious = PersistentCompiledArtifactCachePublisher.MoveExistingEntryAside(finalPath, previousPath);
            try
            {
                Directory.Move(tempPath, finalPath);
                PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(_rootDirectory, finalPath);
                PersistentCompiledArtifactCachePublisher.ValidateEntryShape(finalPath);
            }
            catch
            {
                PersistentCompiledArtifactCachePublisher.RestorePreviousEntry(finalPath, previousPath, movedPrevious);
                throw;
            }

            PersistentCompiledArtifactCachePublisher.DeleteEntryIfExists(previousPath);
        }
        finally
        {
            PersistentCompiledArtifactCachePublisher.DeleteEntryIfExists(tempPath);
        }
    }

    private static async ValueTask<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ??
               throw new JsonException("empty json file");
    }

    private static async ValueTask WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = DurableCreate(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static async ValueTask WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = DurableCreate(path);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private async ValueTask<T> WithEntryLockAsync<T>(
        string cacheKey,
        Func<ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        var entryLock = _entryLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await entryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock = await PersistentCacheEntryLock
                .AcquireAsync(_rootDirectory, cacheKey, cancellationToken)
                .ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }
        finally
        {
            entryLock.Release();
        }
    }

    private async ValueTask WithEntryLockAsync(
        string cacheKey,
        Func<ValueTask> action,
        CancellationToken cancellationToken)
    {
        var entryLock = _entryLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await entryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock = await PersistentCacheEntryLock
                .AcquireAsync(_rootDirectory, cacheKey, cancellationToken)
                .ConfigureAwait(false);
            await action().ConfigureAwait(false);
        }
        finally
        {
            entryLock.Release();
        }
    }

    private void Quarantine(string entryPath)
    {
        if (!Directory.Exists(entryPath))
        {
            return;
        }

        var quarantineRoot = Path.Combine(_rootDirectory, "quarantine");
        Directory.CreateDirectory(quarantineRoot);
        var target = Path.Combine(
            quarantineRoot,
            Path.GetFileName(entryPath) + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N"));
        Directory.Move(entryPath, target);
    }

    private static FileStream DurableCreate(string path)
        => new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);

    private static string CacheInvalidReason(Exception exception)
        => exception switch
        {
            SandboxRuntimeException runtime => runtime.Error.Code.ToString(),
            JsonException => "InvalidJson",
            IOException => "IoFailure",
            UnauthorizedAccessException => "Unauthorized",
            ArgumentException => "InvalidMetadata",
            _ => exception.GetType().Name
        };
}

public sealed record CompiledCacheLookup(
    CompiledCacheStatus Status,
    CompiledArtifact? Artifact,
    string? InvalidReason = null);
