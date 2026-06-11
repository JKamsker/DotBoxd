namespace SafeIR.Compiler;

using System.Security.Cryptography;
using System.Text.Json;
using SafeIR;
using SafeIR.Verifier;

public sealed class PersistentCompiledArtifactCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    private readonly string _rootDirectory;

    public PersistentCompiledArtifactCache(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public bool EntryExists(string cacheKey) => Directory.Exists(EntryPath(cacheKey));

    public async ValueTask<CompiledCacheLookup> TryReadAsync(
        string cacheKey,
        ExecutionPlan plan,
        IGeneratedAssemblyVerifier verifier,
        VerificationPolicy policy,
        CancellationToken cancellationToken)
    {
        var entryPath = EntryPath(cacheKey);
        if (!Directory.Exists(entryPath)) {
            return new CompiledCacheLookup(CompiledCacheStatus.Miss, null);
        }

        try {
            var manifest = await ReadJsonAsync<ArtifactManifest>(Path.Combine(entryPath, "manifest.json"), cancellationToken)
                .ConfigureAwait(false);
            ValidateManifest(cacheKey, plan, manifest);
            var assemblyBytes = await File.ReadAllBytesAsync(Path.Combine(entryPath, "module.dll"), cancellationToken)
                .ConfigureAwait(false);
            var verification = await verifier.VerifyAsync(assemblyBytes, manifest, policy, cancellationToken).ConfigureAwait(false);
            if (!verification.Succeeded) {
                throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.VerifierFailure, "cached artifact failed verification"));
            }

            return new CompiledCacheLookup(CompiledCacheStatus.Hit, new CompiledArtifact(
                assemblyBytes,
                verification.AssemblyHash,
                manifest,
                verification,
                (_, _) => throw new InvalidOperationException("cached artifact entrypoint is loaded by the compiler"),
                CompiledCacheStatus.Hit));
        }
        catch (Exception ex) when (ex is IOException or JsonException or SandboxRuntimeException or UnauthorizedAccessException) {
            Quarantine(entryPath);
            return new CompiledCacheLookup(CompiledCacheStatus.Invalid, null);
        }
    }

    public async ValueTask WriteAsync(
        string cacheKey,
        ExecutionPlan plan,
        byte[] assemblyBytes,
        ArtifactManifest manifest,
        VerificationResult verification,
        CancellationToken cancellationToken)
    {
        ValidateManifest(cacheKey, plan, manifest);
        var finalPath = EntryPath(cacheKey);
        var tempPath = Path.Combine(_rootDirectory, ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try {
            await File.WriteAllBytesAsync(Path.Combine(tempPath, "module.dll"), assemblyBytes, cancellationToken)
                .ConfigureAwait(false);
            await WriteJsonAsync(Path.Combine(tempPath, "manifest.json"), manifest, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(Path.Combine(tempPath, "verification.json"), verification, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            if (Directory.Exists(finalPath)) {
                Directory.Delete(finalPath, recursive: true);
            }

            Directory.Move(tempPath, finalPath);
        }
        finally {
            if (Directory.Exists(tempPath)) {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    public string EntryPath(string cacheKey)
    {
        if (!IsHexHash(cacheKey)) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.CacheInvalid, "cache key is not path safe"));
        }

        return Path.Combine(_rootDirectory, cacheKey[..2], cacheKey[2..4], cacheKey);
    }

    private static void ValidateManifest(string cacheKey, ExecutionPlan plan, ArtifactManifest manifest)
    {
        if (manifest.ArtifactVersion != 1 ||
            manifest.CacheKey != cacheKey ||
            manifest.ModuleHash != plan.ModuleHash ||
            manifest.PlanHash != plan.PlanHash ||
            manifest.PolicyHash != plan.PolicyHash ||
            manifest.BindingManifestHash != plan.BindingManifestHash ||
            manifest.CompilerVersion != CacheKeyBuilder.CompilerVersion ||
            manifest.RuntimeFacadeHash != CacheKeyBuilder.RuntimeFacadeHash ||
            manifest.LanguageVersion != CacheKeyBuilder.LanguageVersion ||
            manifest.TargetFramework != CacheKeyBuilder.TargetFramework) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.CacheInvalid, "cached artifact manifest does not match current plan"));
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
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private void Quarantine(string entryPath)
    {
        if (!Directory.Exists(entryPath)) {
            return;
        }

        var quarantineRoot = Path.Combine(_rootDirectory, "quarantine");
        Directory.CreateDirectory(quarantineRoot);
        var target = Path.Combine(quarantineRoot, Path.GetFileName(entryPath) + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Directory.Move(entryPath, target);
    }

    private static bool IsHexHash(string cacheKey)
        => cacheKey.Length >= 64 && cacheKey.All(Uri.IsHexDigit);
}

public sealed record CompiledCacheLookup(CompiledCacheStatus Status, CompiledArtifact? Artifact);
