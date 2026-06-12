namespace SafeIR.Verifier;

internal static class ManifestIdentityVerifier
{
    public static void Verify(
        ArtifactManifest manifest,
        VerificationPolicy policy,
        List<VerificationDiagnostic> diagnostics)
    {
        if (policy.ExpectedManifestIdentity is not { } expected)
        {
            return;
        }

        Compare("artifact version", manifest.ArtifactVersion, expected.ArtifactVersion, diagnostics);
        Compare("cache key", manifest.CacheKey, expected.CacheKey, diagnostics);
        Compare("module hash", manifest.ModuleHash, expected.ModuleHash, diagnostics);
        Compare("plan hash", manifest.PlanHash, expected.PlanHash, diagnostics);
        Compare("policy hash", manifest.PolicyHash, expected.PolicyHash, diagnostics);
        Compare("binding manifest hash", manifest.BindingManifestHash, expected.BindingManifestHash, diagnostics);
        Compare("runtime facade hash", manifest.RuntimeFacadeHash, expected.RuntimeFacadeHash, diagnostics);
        Compare("compiler version", manifest.CompilerVersion, expected.CompilerVersion, diagnostics);
        Compare("type-system version", manifest.TypeSystemVersion, expected.TypeSystemVersion, diagnostics);
        Compare("effect-analysis version", manifest.EffectAnalysisVersion, expected.EffectAnalysisVersion, diagnostics);
        Compare("verifier version", manifest.VerifierVersion, expected.VerifierVersion, diagnostics);
        Compare("language version", manifest.LanguageVersion, expected.LanguageVersion, diagnostics);
        Compare("target framework", manifest.TargetFramework, expected.TargetFramework, diagnostics);
        CompareFlags(manifest.OptimizationFlags, expected.OptimizationFlags, diagnostics);
    }

    private static void Compare(
        string field,
        string actual,
        string? expected,
        List<VerificationDiagnostic> diagnostics)
    {
        if (expected is not null && !StringComparer.Ordinal.Equals(actual, expected))
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-MANIFEST-IDENTITY",
                $"manifest {field} does not match expected verification context"));
        }
    }

    private static void Compare(
        string field,
        int actual,
        int? expected,
        List<VerificationDiagnostic> diagnostics)
    {
        if (expected is not null && actual != expected.Value)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-MANIFEST-IDENTITY",
                $"manifest {field} does not match expected verification context"));
        }
    }

    private static void CompareFlags(
        IReadOnlyList<string>? actual,
        IReadOnlyList<string>? expected,
        List<VerificationDiagnostic> diagnostics)
    {
        if (expected is not null &&
            (actual is null || !actual.SequenceEqual(expected, StringComparer.Ordinal)))
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-MANIFEST-IDENTITY",
                "manifest optimization flags do not match expected verification context"));
        }
    }
}
