namespace SafeIR.Verifier;

public sealed record ArtifactManifest(
    int ArtifactVersion,
    string CacheKey,
    string ModuleHash,
    string PlanHash,
    string PolicyHash,
    string BindingManifestHash,
    string RuntimeFacadeHash,
    string CompilerVersion,
    string TypeSystemVersion,
    string EffectAnalysisVersion,
    string VerifierVersion,
    string LanguageVersion,
    string TargetFramework,
    IReadOnlyList<string> OptimizationFlags,
    string AssemblyHash,
    DateTimeOffset CreatedAt);

public sealed record VerificationDiagnostic(string Code, string Message);

public sealed record VerificationResult(
    bool Succeeded,
    IReadOnlyList<VerificationDiagnostic> Diagnostics,
    string AssemblyHash,
    string VerifierVersion,
    DateTimeOffset VerifiedAt);

public interface IGeneratedAssemblyVerifier
{
    ValueTask<VerificationResult> VerifyAsync(
        ReadOnlyMemory<byte> assemblyBytes,
        ArtifactManifest manifest,
        VerificationPolicy policy,
        CancellationToken cancellationToken);
}
