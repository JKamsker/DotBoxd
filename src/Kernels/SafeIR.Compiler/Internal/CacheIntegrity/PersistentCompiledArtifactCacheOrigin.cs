namespace SafeIR.Compiler.Internal;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SafeIR;
using SafeIR.Verifier;

internal static class PersistentCompiledArtifactCacheOrigin
{
    public const string ProofFileName = "origin.json";

    private const int ProofVersion = 1;
    private const int OriginKeyByteLength = 32;
    private const string ProofAlgorithm = "HMAC-SHA256";

    private static readonly SemaphoreSlim OriginKeyGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async ValueTask WriteProofAsync(
        string entryPath,
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        ArtifactManifest manifest,
        VerificationResult verification,
        byte[] assemblyBytes,
        CancellationToken cancellationToken)
    {
        var signature = await SignAsync(
                cacheKey,
                plan,
                entrypoint,
                manifest,
                verification,
                assemblyBytes,
                cancellationToken)
            .ConfigureAwait(false);
        var proof = new CacheOriginProof(ProofVersion, ProofAlgorithm, signature);
        var path = Path.Combine(entryPath, ProofFileName);
        await using var stream = DurableCreate(path);
        await JsonSerializer.SerializeAsync(stream, proof, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    public static async ValueTask ValidateProofAsync(
        string entryPath,
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        ArtifactManifest manifest,
        VerificationResult verification,
        byte[] assemblyBytes,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(entryPath, ProofFileName);
        CacheOriginProof proof;
        await using (var stream = File.OpenRead(path))
        {
            proof = await JsonSerializer.DeserializeAsync<CacheOriginProof>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false) ??
                throw CacheInvalid("cached artifact origin proof is empty");
        }

        if (proof.Version != ProofVersion ||
            !StringComparer.Ordinal.Equals(proof.Algorithm, ProofAlgorithm) ||
            string.IsNullOrWhiteSpace(proof.Signature))
        {
            throw CacheInvalid("cached artifact origin proof is invalid");
        }

        var expected = await SignAsync(
                cacheKey,
                plan,
                entrypoint,
                manifest,
                verification,
                assemblyBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (!FixedTimeHexEquals(expected, proof.Signature))
        {
            throw CacheInvalid("cached artifact origin proof does not match current host");
        }
    }

    private static async ValueTask<string> SignAsync(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        ArtifactManifest manifest,
        VerificationResult verification,
        byte[] assemblyBytes,
        CancellationToken cancellationToken)
    {
        var key = await ReadOrCreateOriginKeyAsync(cancellationToken).ConfigureAwait(false);
        return ComputeSignature(key, cacheKey, plan, entrypoint, manifest, verification, assemblyBytes);
    }

    private static string ComputeSignature(
        byte[] key,
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        ArtifactManifest manifest,
        VerificationResult verification,
        byte[] assemblyBytes)
    {
        using var hmac = new HMACSHA256(key);
        AddString(hmac, "safeir-compiled-cache-origin");
        AddInt32(hmac, ProofVersion);
        AddString(hmac, cacheKey);
        AddString(hmac, entrypoint);
        AddManifest(hmac, manifest);
        AddVerification(hmac, verification);
        AddString(hmac, plan.ModuleHash);
        AddString(hmac, plan.PlanHash);
        AddString(hmac, plan.PolicyHash);
        AddString(hmac, plan.BindingManifestHash);
        AddInt32(hmac, assemblyBytes.Length);
        hmac.TransformFinalBlock(assemblyBytes, 0, assemblyBytes.Length);
        return Convert.ToHexString(hmac.Hash!).ToLowerInvariant();
    }

    private static void AddManifest(HMAC hmac, ArtifactManifest manifest)
    {
        AddInt32(hmac, manifest.ArtifactVersion);
        AddString(hmac, manifest.CacheKey);
        AddString(hmac, manifest.ModuleHash);
        AddString(hmac, manifest.PlanHash);
        AddString(hmac, manifest.PolicyHash);
        AddString(hmac, manifest.BindingManifestHash);
        AddString(hmac, manifest.RuntimeFacadeHash);
        AddString(hmac, manifest.CompilerVersion);
        AddString(hmac, manifest.TypeSystemVersion);
        AddString(hmac, manifest.EffectAnalysisVersion);
        AddString(hmac, manifest.VerifierVersion);
        AddString(hmac, manifest.LanguageVersion);
        AddString(hmac, manifest.TargetFramework);
        AddStrings(hmac, manifest.OptimizationFlags);
        AddString(hmac, manifest.AssemblyHash);
        AddInt64(hmac, manifest.CreatedAt.UtcTicks);
    }

    private static void AddVerification(HMAC hmac, VerificationResult verification)
    {
        AddInt32(hmac, verification.Succeeded ? 1 : 0);
        AddString(hmac, verification.AssemblyHash);
        AddString(hmac, verification.VerifierVersion);
        AddInt64(hmac, verification.VerifiedAt.UtcTicks);
    }

    private static void AddStrings(HMAC hmac, IReadOnlyList<string> values)
    {
        AddInt32(hmac, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            AddString(hmac, values[i]);
        }
    }

    private static void AddString(HMAC hmac, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AddInt32(hmac, bytes.Length);
        if (bytes.Length > 0)
        {
            hmac.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
    }

    private static void AddInt32(HMAC hmac, int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hmac.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }

    private static void AddInt64(HMAC hmac, long value)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hmac.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }

    private static async ValueTask<byte[]> ReadOrCreateOriginKeyAsync(CancellationToken cancellationToken)
    {
        await OriginKeyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var keyPath = PersistentCompiledArtifactCacheOriginKeyPath.Get();
            var keyDirectory = Path.GetDirectoryName(keyPath)!;
            Directory.CreateDirectory(keyDirectory);
            PersistentCompiledArtifactCacheOriginKeyGuard.HardenDirectory(keyDirectory);
            PersistentCompiledArtifactCacheOriginKeyGuard.ValidateDirectory(keyDirectory);

            if (await TryAdoptExistingKeyAsync(keyPath, cancellationToken).ConfigureAwait(false) is { } existingKey)
            {
                return existingKey;
            }

            return await CreateOriginKeyAsync(keyPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            OriginKeyGate.Release();
        }
    }

    private static async ValueTask<byte[]?> TryAdoptExistingKeyAsync(string keyPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(keyPath))
        {
            return null;
        }

        try
        {
            // Fail closed before trusting the key: a key file readable or replaceable by a
            // broad principal lets that actor forge valid origin proofs or rotate the signing
            // root. A key whose permissions cannot be verified, or that is otherwise
            // inaccessible, is treated as untrusted rather than fatal so a stale or corrupted
            // file does not permanently brick compiled-cache writes.
            PersistentCompiledArtifactCacheOriginKeyGuard.ValidateFile(keyPath);
            var existingKey = await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false);
            if (existingKey.Length == OriginKeyByteLength)
            {
                return existingKey;
            }
        }
        catch (Exception ex) when (ex is SandboxRuntimeException or IOException or UnauthorizedAccessException)
        {
            // Untrusted or unreadable key: fall through to scrub and recreate.
        }

        TryScrubFile(keyPath);
        return null;
    }

    private static async ValueTask<byte[]> CreateOriginKeyAsync(string keyPath, CancellationToken cancellationToken)
    {
        // A pre-existing file may linger if adoption left it in place (for example a key with
        // an ACL that locked out the current principal). DurableCreate uses FileMode.CreateNew
        // and would throw, so scrub any residue first.
        TryScrubFile(keyPath);

        var key = RandomNumberGenerator.GetBytes(OriginKeyByteLength);
        await using (var stream = DurableCreate(keyPath))
        {
            await stream.WriteAsync(key, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        PersistentCompiledArtifactCacheOriginKeyGuard.HardenFile(keyPath);
        PersistentCompiledArtifactCacheOriginKeyGuard.ValidateFile(keyPath);
        return key;
    }

    private static void TryScrubFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file's ACL may have locked out the current principal. Restore owner access
            // before retrying the delete so a previously corrupted key can be replaced.
        }

        try
        {
            PersistentCompiledArtifactCacheOriginKeyGuard.RestoreOwnerAccess(path);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: if the file still cannot be removed, DurableCreate will surface the
            // failure to the caller, which fails the compiled-cache write closed.
        }
    }

    private static bool FixedTimeHexEquals(string expected, string actual)
    {
        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var actualBytes = Convert.FromHexString(actual);
            return expectedBytes.Length == actualBytes.Length &&
                CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static FileStream DurableCreate(string path)
        => new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough | FileOptions.Asynchronous);

    private static SandboxRuntimeException CacheInvalid(string message)
        => new(new SandboxError(SandboxErrorCode.CacheInvalid, message));

    private sealed record CacheOriginProof(int Version, string Algorithm, string Signature);
}
