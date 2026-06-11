using SafeIR.Compiler;
using SafeIR.Interpreter;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class BackendIsolationTests
{
    [Fact]
    public void Interpreter_assembly_has_no_compiler_or_verifier_dependency()
    {
        var references = ReferencedAssemblyNames(typeof(SandboxInterpreter).Assembly);

        Assert.DoesNotContain("SafeIR.Compiler", references);
        Assert.DoesNotContain("SafeIR.Verifier", references);
    }

    [Fact]
    public void Compiler_assembly_has_no_interpreter_dependency()
    {
        var references = ReferencedAssemblyNames(typeof(ISandboxCompiler).Assembly);

        Assert.DoesNotContain("SafeIR.Interpreter", references);
    }

    [Fact]
    public void Dynamic_method_artifact_rejects_assembly_bytes()
    {
        var ex = Assert.Throws<ArgumentException>(() => new CompiledArtifact(
            [0x4d, 0x5a],
            "dynamic-artifact",
            Manifest("dynamic-artifact"),
            Verification("dynamic-artifact"),
            (_, _) => SandboxValue.Unit,
            CompiledRuntimeFormKind.DynamicMethod));

        Assert.Equal("AssemblyBytes", ex.ParamName);
    }

    [Fact]
    public void Loaded_assembly_artifact_requires_assembly_bytes()
    {
        var ex = Assert.Throws<ArgumentException>(() => new CompiledArtifact(
            [],
            "assembly-artifact",
            Manifest("assembly-artifact"),
            Verification("assembly-artifact"),
            (_, _) => SandboxValue.Unit,
            CompiledRuntimeFormKind.LoadedAssembly));

        Assert.Equal("AssemblyBytes", ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_rejects_failed_verification_or_gate()
    {
        var ex = Assert.Throws<ArgumentException>(() => new CompiledArtifact(
            [],
            "dynamic-artifact",
            Manifest("dynamic-artifact"),
            new VerificationResult(
                false,
                [new VerificationDiagnostic("V-GATE", "dynamic method gate failed")],
                "dynamic-artifact",
                "verifier-version",
                DateTimeOffset.UtcNow),
            (_, _) => SandboxValue.Unit,
            CompiledRuntimeFormKind.DynamicMethod));

        Assert.Equal("verification", ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_rejects_hash_mismatch()
    {
        var ex = Assert.Throws<ArgumentException>(() => new CompiledArtifact(
            [],
            "dynamic-artifact",
            Manifest("other-artifact"),
            Verification("dynamic-artifact"),
            (_, _) => SandboxValue.Unit,
            CompiledRuntimeFormKind.DynamicMethod));

        Assert.Equal("assemblyHash", ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_rejects_unknown_runtime_form()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new CompiledArtifact(
            [],
            "dynamic-artifact",
            Manifest("dynamic-artifact"),
            Verification("dynamic-artifact"),
            (_, _) => SandboxValue.Unit,
            (CompiledRuntimeFormKind)99));

        Assert.Equal("runtimeForm", ex.ParamName);
    }

    private static string[] ReferencedAssemblyNames(System.Reflection.Assembly assembly)
        => assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToArray();

    private static ArtifactManifest Manifest(string artifactHash)
        => new(
            1,
            "cache-key",
            "module-hash",
            "plan-hash",
            "policy-hash",
            "binding-hash",
            "runtime-hash",
            "compiler-version",
            "verifier-version",
            "1.0.0",
            "net10.0",
            [],
            artifactHash,
            DateTimeOffset.UtcNow);

    private static VerificationResult Verification(string artifactHash)
        => new(true, [], artifactHash, "verifier-version", DateTimeOffset.UtcNow);
}
