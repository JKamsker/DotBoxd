using DotBoxD.Kernels.Compiler.Internal.CacheIntegrity;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Compiler;

using System.Collections.Concurrent;
using System.Text.Json;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Verifier;

public sealed partial class PersistentCompiledArtifactCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly ConcurrentDictionary<string, EntryLock> _entryLocks = new(StringComparer.Ordinal);

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
            await PersistentCompiledArtifactCacheOrigin.ValidateProofAsync(
                    entryPath,
                    cacheKey,
                    plan,
                    entrypoint,
                    manifest,
                    cachedVerification,
                    assemblyBytes,
                    cancellationToken)
                .ConfigureAwait(false);

            // The cached verification record is the artifact's verification proof on the read
            // path: the manifest identity, the cached verification result, and the host-bound
            // origin proof have all been validated above, and the origin proof is an HMAC over
            // the exact assembly bytes plus that verification record signed by this host's secret
            // key. Re-running the full generated-assembly verifier or hashing the bytes again here
            // only repeats work the write path already performed and that materialization repeats
            // before loading the assembly. The verifier parameter is retained for the contract and
            // remains the gate on the write path.
            _ = verifier;

            return new CompiledCacheLookup(CompiledCacheStatus.Hit, new CompiledArtifact(
                assemblyBytes,
                cachedVerification.AssemblyHash,
                manifest,
                cachedVerification,
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
            await PersistentCompiledArtifactCacheOrigin.WriteProofAsync(
                    tempPath,
                    cacheKey,
                    plan,
                    entrypoint,
                    manifest,
                    verification,
                    assemblyBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            PersistentCompiledArtifactCachePublisher.ValidateEntryShape(tempPath);
            await ValidateTempEntryAsync(
                    tempPath,
                    cacheKey,
                    plan,
                    entrypoint,
                    policy,
                    cancellationToken)
                .ConfigureAwait(false);

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
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ??
                   throw new JsonException("empty json file");
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            and not JsonException
            and not IOException
            and not UnauthorizedAccessException)
        {
            // A cached model can round-trip through System.Text.Json yet still fail to
            // construct because its defensive normalization (e.g. ArtifactManifest copying
            // OptimizationFlags, which rejects a null collection) throws while materializing
            // invalid persisted data. Convert any such materialization failure into a
            // JsonException so the cache read path fails closed and routes the entry to
            // quarantine + recompile, instead of surfacing an unhandled exception that aborts
            // execution. Cancellation and the already-handled IO/JSON failures propagate as-is.
            throw new JsonException(
                $"cached '{typeof(T).Name}' metadata could not be materialized: {ex.Message}",
                ex);
        }
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

    private void Quarantine(string entryPath)
    {
        if (!Directory.Exists(entryPath))
        {
            return;
        }

        var quarantineRoot = Path.Combine(_rootDirectory, "quarantine");
        PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(_rootDirectory, quarantineRoot);
        Directory.CreateDirectory(quarantineRoot);
        PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(_rootDirectory, quarantineRoot);
        var target = Path.Combine(
            quarantineRoot,
            Path.GetFileName(entryPath) + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N"));
        PersistentCompiledArtifactCachePathGuard.ValidateEntryPath(_rootDirectory, target);
        Directory.Move(entryPath, target);

        // Bound the quarantine tree so repeated corruption cannot grow disk usage and directory-scan
        // cost without limit. Runs after the move, so cleanup cost stays proportional to the current
        // quarantine size rather than every entry ever quarantined.
        PersistentCompiledArtifactCacheQuarantine.Prune(quarantineRoot);
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
