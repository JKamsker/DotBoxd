using SafeIR.Compiler;
using SafeIR.Hosting;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class CompiledArtifactGuardTests
{
    [Fact]
    public async Task Compiled_artifact_manifest_mismatch_is_rejected_before_delegate_runs()
    {
        var compiler = new TamperedLoadedAssemblyCompiler(artifact => artifact with
        {
            Manifest = artifact.Manifest with { PlanHash = "other-plan" }
        });
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.False(compiler.DelegateExecuted);
    }

    [Fact]
    public async Task Compiled_artifact_runtime_form_mismatch_is_rejected_before_delegate_runs()
    {
        var compiler = new TamperedLoadedAssemblyCompiler(artifact => artifact with
        {
            RuntimeForm = CompiledRuntimeFormKind.DynamicMethod
        });
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.False(compiler.DelegateExecuted);
    }

    [Fact]
    public async Task Dynamic_method_artifact_is_rejected_before_delegate_runs()
    {
        var compiler = new DynamicCompiler();
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.False(compiler.DelegateExecuted);
    }

    [Fact]
    public async Task Dynamic_method_artifact_with_stale_gate_version_is_rejected_before_delegate_runs()
    {
        var compiler = new DynamicCompiler(artifact => artifact with
        {
            Verification = artifact.Verification with { VerifierVersion = "stale-gate" }
        });
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.False(compiler.DelegateExecuted);
    }

    [Theory]
    [InlineData("cache")]
    [InlineData("compiler")]
    [InlineData("type-system")]
    [InlineData("analysis")]
    [InlineData("verifier")]
    [InlineData("runtime")]
    [InlineData("language")]
    [InlineData("target")]
    [InlineData("flags")]
    public async Task Stale_manifest_identity_is_rejected_before_delegate_runs(string staleField)
    {
        var compiler = new TamperedLoadedAssemblyCompiler(artifact => artifact with
        {
            Manifest = StaleManifest(artifact.Manifest, staleField)
        });
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.False(compiler.DelegateExecuted);
    }

    [Fact]
    public async Task Verified_bytes_returning_wrong_type_fail_at_host_boundary()
    {
        var compiler = new LoadedBytesCompiler(CompiledArtifactTestFactory.BuildBoolAssembly(parameterCount: 2, value: true));
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Contains("return type", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.NotNull(result.ArtifactHash);
    }

    [Fact]
    public async Task Loaded_assembly_bytes_are_snapshotted_when_artifact_is_created()
    {
        var host = HostWithCompiler(new SourceByteMutationCompiler());
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(SandboxValue.FromInt32(123), result.Value);
    }

    [Fact]
    public async Task Loaded_assembly_uses_materialized_delegate_not_supplied_delegate()
    {
        var compiler = new LoadedAssemblyWithStaleDelegateCompiler();
        var host = HostWithCompiler(compiler);
        var plan = await PreparePurePlanAsync(host);

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromInt32(123), result.Value);
        Assert.False(compiler.SuppliedDelegateExecuted);
    }

    private static SandboxHost HostWithCompiler(ISandboxCompiler compiler)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

    private static async Task<ExecutionPlan> PreparePurePlanAsync(SandboxHost host)
    {
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        SandboxHost host,
        ExecutionPlan plan)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static ArtifactManifest StaleManifest(ArtifactManifest manifest, string field)
        => field switch
        {
            "cache" => manifest with { CacheKey = "stale-cache-key" },
            "compiler" => manifest with { CompilerVersion = "stale-compiler" },
            "type-system" => manifest with { TypeSystemVersion = "stale-type-system" },
            "analysis" => manifest with { EffectAnalysisVersion = "stale-analysis" },
            "verifier" => manifest with { VerifierVersion = "stale-verifier" },
            "runtime" => manifest with { RuntimeFacadeHash = "stale-runtime" },
            "language" => manifest with { LanguageVersion = "0.0.0" },
            "target" => manifest with { TargetFramework = "net9.0" },
            "flags" => manifest with { OptimizationFlags = ["opt"] },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown manifest field.")
        };

    private sealed class TamperedLoadedAssemblyCompiler(Func<CompiledArtifact, CompiledArtifact> tamper) : ISandboxCompiler
    {
        public bool DelegateExecuted { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            var artifact = CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123),
                (_, _) =>
                {
                    DelegateExecuted = true;
                    return SandboxValue.FromInt32(123);
                });
            return ValueTask.FromResult(tamper(artifact));
        }
    }

    private sealed class DynamicCompiler(Func<CompiledArtifact, CompiledArtifact>? tamper = null) : ISandboxCompiler
    {
        public bool DelegateExecuted { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            var artifact = CompiledArtifactTestFactory.DynamicMethod(
                plan,
                (_, _) =>
                {
                    DelegateExecuted = true;
                    return SandboxValue.FromInt32(123);
                });
            return ValueTask.FromResult(tamper?.Invoke(artifact) ?? artifact);
        }
    }

    private sealed class LoadedBytesCompiler(byte[] assemblyBytes) : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(CompiledArtifactTestFactory.LoadedAssembly(plan, assemblyBytes));
    }

    private sealed class SourceByteMutationCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            var bytes = CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123);
            var artifact = CompiledArtifactTestFactory.LoadedAssembly(plan, bytes);
            bytes[0] ^= 0xff;
            return ValueTask.FromResult(artifact);
        }
    }

    private sealed class LoadedAssemblyWithStaleDelegateCompiler : ISandboxCompiler
    {
        public bool SuppliedDelegateExecuted { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 123),
                (_, _) =>
                {
                    SuppliedDelegateExecuted = true;
                    return SandboxValue.FromInt32(999);
                }));
    }
}
