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

public sealed record VerificationManifestIdentity(
    int? ArtifactVersion = null,
    string? CacheKey = null,
    string? ModuleHash = null,
    string? PlanHash = null,
    string? PolicyHash = null,
    string? BindingManifestHash = null,
    string? RuntimeFacadeHash = null,
    string? CompilerVersion = null,
    string? TypeSystemVersion = null,
    string? EffectAnalysisVersion = null,
    string? VerifierVersion = null,
    string? LanguageVersion = null,
    string? TargetFramework = null,
    IReadOnlyList<string>? OptimizationFlags = null)
{
    public static VerificationManifestIdentity FromManifest(ArtifactManifest manifest)
        => new(
            manifest.ArtifactVersion,
            manifest.CacheKey,
            manifest.ModuleHash,
            manifest.PlanHash,
            manifest.PolicyHash,
            manifest.BindingManifestHash,
            manifest.RuntimeFacadeHash,
            manifest.CompilerVersion,
            manifest.TypeSystemVersion,
            manifest.EffectAnalysisVersion,
            manifest.VerifierVersion,
            manifest.LanguageVersion,
            manifest.TargetFramework,
            manifest.OptimizationFlags.ToArray());
}

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
