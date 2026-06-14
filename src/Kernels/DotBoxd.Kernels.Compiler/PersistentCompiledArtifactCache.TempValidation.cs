namespace DotBoxd.Kernels.Compiler;

using System.Security.Cryptography;
using DotBoxd.Kernels;
using DotBoxd.Kernels.Verifier;

public sealed partial class PersistentCompiledArtifactCache
{
    private static async ValueTask ValidateTempEntryAsync(
        string tempPath,
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        VerificationPolicy policy,
        CancellationToken cancellationToken)
    {
        var tempManifest = await ReadJsonAsync<ArtifactManifest>(
                Path.Combine(tempPath, "manifest.json"),
                cancellationToken)
            .ConfigureAwait(false);
        PersistentCompiledArtifactCacheValidator.ValidateManifest(cacheKey, plan, entrypoint, tempManifest, policy);
        var tempVerification = await ReadJsonAsync<VerificationResult>(
                Path.Combine(tempPath, "verification.json"),
                cancellationToken)
            .ConfigureAwait(false);
        PersistentCompiledArtifactCacheValidator.ValidateVerification(tempManifest, tempVerification, policy);
        var tempAssembly = await File.ReadAllBytesAsync(Path.Combine(tempPath, "module.dll"), cancellationToken)
            .ConfigureAwait(false);
        var tempHash = Convert.ToHexString(SHA256.HashData(tempAssembly)).ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(tempHash, tempManifest.AssemblyHash)) {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.CacheInvalid,
                "temporary cache artifact hash does not match manifest"));
        }

        await PersistentCompiledArtifactCacheOrigin.ValidateProofAsync(
                tempPath,
                cacheKey,
                plan,
                entrypoint,
                tempManifest,
                tempVerification,
                tempAssembly,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
