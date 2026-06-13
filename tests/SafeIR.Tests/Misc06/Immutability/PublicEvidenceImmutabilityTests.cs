using SafeIR.Compiler;
using SafeIR.Validation.Internal;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class PublicEvidenceImmutabilityTests
{
    [Fact]
    public void Module_validation_failure_snapshots_diagnostics()
    {
        var diagnostics = new List<SandboxDiagnostic>
        {
            new("E-ONE", "first")
        };
        var result = ModuleValidationResult.Failure(diagnostics);

        diagnostics.Add(new SandboxDiagnostic("E-TWO", "second"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("E-ONE", diagnostic.Code);
        Assert.False(result.Diagnostics is List<SandboxDiagnostic>);
    }

    [Fact]
    public void Module_validation_success_snapshots_analysis_collections()
    {
        var diagnostics = new List<SandboxDiagnostic>();
        var functions = new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal)
        {
            ["main"] = new(SandboxType.I32, SandboxEffect.Audit, true)
        };
        var requiredCapabilities = new HashSet<string>(StringComparer.Ordinal)
        {
            "log.write"
        };
        var mainReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            "log.write"
        };
        var bindingReferences = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["main"] = mainReferences
        };

        var result = new ModuleValidationResult(
            true,
            diagnostics,
            functions,
            SandboxEffect.Audit,
            requiredCapabilities,
            bindingReferences);

        diagnostics.Add(new SandboxDiagnostic("E-MUTATED", "mutated"));
        functions["helper"] = new FunctionAnalysis(SandboxType.I32, SandboxEffect.Cpu, true);
        requiredCapabilities.Add("file.writeText");
        mainReferences.Add("file.writeText");
        bindingReferences["helper"] = new HashSet<string>(StringComparer.Ordinal) { "random" };

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.Functions);
        Assert.Equal("log.write", Assert.Single(result.RequiredCapabilities));
        Assert.True(result.BindingReferences.TryGetValue("main", out var references));
        Assert.Equal("log.write", Assert.Single(references));
        Assert.False(result.Functions is Dictionary<string, FunctionAnalysis>);
        Assert.False(result.RequiredCapabilities is HashSet<string>);
        Assert.False(result.BindingReferences is Dictionary<string, IReadOnlySet<string>>);
        Assert.False(references is HashSet<string>);
    }

    [Fact]
    public void Verifier_evidence_models_snapshot_collection_inputs()
    {
        var flags = new List<string> { "boxed-values" };
        var diagnostics = new List<VerificationDiagnostic>
        {
            new("V-ONE", "first")
        };

        var manifest = Manifest("artifact-hash", flags);
        var identity = new VerificationManifestIdentity(OptimizationFlags: flags);
        var result = new VerificationResult(
            false,
            diagnostics,
            "artifact-hash",
            "verifier-version",
            DateTimeOffset.UtcNow);

        flags[0] = "mutated";
        diagnostics.Add(new VerificationDiagnostic("V-TWO", "second"));

        Assert.Equal("boxed-values", Assert.Single(manifest.OptimizationFlags));
        Assert.Equal("boxed-values", Assert.Single(identity.OptimizationFlags!));
        Assert.Equal("V-ONE", Assert.Single(result.Diagnostics).Code);
        Assert.False(manifest.OptimizationFlags is List<string>);
        Assert.False(identity.OptimizationFlags is List<string>);
        Assert.False(result.Diagnostics is List<VerificationDiagnostic>);
    }

    [Fact]
    public void Compiled_artifact_keeps_stable_manifest_and_verification_evidence()
    {
        var artifactHash = "artifact-hash";
        var flags = new List<string> { "boxed-values" };
        var diagnostics = new List<VerificationDiagnostic>();
        var manifest = Manifest(artifactHash, flags);
        var verification = new VerificationResult(
            true,
            diagnostics,
            artifactHash,
            "verifier-version",
            DateTimeOffset.UtcNow);

        var artifact = new CompiledArtifact(
            [0x4d, 0x5a],
            artifactHash,
            manifest,
            verification,
            (_, _) => SandboxValue.Unit,
            CompiledRuntimeFormKind.LoadedAssembly);

        flags.Clear();
        diagnostics.Add(new VerificationDiagnostic("V-MUTATED", "mutated"));

        Assert.Equal("boxed-values", Assert.Single(artifact.Manifest.OptimizationFlags));
        Assert.Empty(artifact.Verification.Diagnostics);
    }

    private static ArtifactManifest Manifest(string artifactHash, IReadOnlyList<string> flags)
        => new(
            1,
            "cache-key",
            "module-hash",
            "plan-hash",
            "policy-hash",
            "binding-hash",
            "runtime-hash",
            "compiler-version",
            "type-system-version",
            "effect-analysis-version",
            "verifier-version",
            "1.0.0",
            "net10.0",
            flags,
            artifactHash,
            DateTimeOffset.UtcNow);
}
