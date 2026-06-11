using SafeIR;
using SafeIR.Compiler;
using SafeIR.Hosting;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class ExecutionModeSelectionTests
{
    [Fact]
    public async Task Interpreted_mode_executes_ir_without_compiler()
    {
        var compiler = new FailingCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(35, ((I32Value)result.Value!).Value);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Null(result.ArtifactHash);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Auto_mode_uses_interpreter_without_hotness_selector()
    {
        var compiler = new FailingCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Auto });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Null(result.ArtifactHash);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Compiled_mode_without_compiler_falls_back_when_allowed()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Null(result.ArtifactHash);
    }

    [Fact]
    public async Task Compiled_mode_without_compiler_fails_when_fallback_disabled()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Null(result.ArtifactHash);
        Assert.Contains(result.AuditEvents, e => e.Kind == "CompilerUnavailable");
    }

    [Fact]
    public async Task Compiled_mode_invokes_compiler_provided_runtime_form()
    {
        var compiler = new DelegateCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(123, ((I32Value)result.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal("delegate-artifact", result.ArtifactHash);
        Assert.Equal(1, compiler.Calls);
        Assert.Contains(
            result.AuditEvents,
            e => e.Message?.Contains("runtimeForm=DynamicMethod", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Compiled_runtime_unexpected_exception_returns_host_failure()
    {
        var host = HostWithCompiler(new ThrowingDelegateCompiler());
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal("throwing-artifact", result.ArtifactHash);
    }

    [Fact]
    public async Task Compiled_mode_compiler_sandbox_failure_returns_result_when_fallback_disabled()
    {
        var host = HostWithCompiler(new SandboxFailureCompiler(SandboxErrorCode.VerifierFailure));
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.VerifierFailure, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Null(result.ArtifactHash);
        Assert.Contains(result.AuditEvents, e => e.Kind == "CompiledExecutionFailed");
    }

    [Fact]
    public async Task Compiled_mode_unexpected_compiler_exception_returns_host_failure()
    {
        var host = HostWithCompiler(new UnexpectedFailureCompiler());
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Null(result.ArtifactHash);
    }

    private static SandboxHost HostWithCompiler(ISandboxCompiler compiler)
        => SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

    private static ArtifactManifest Manifest(ExecutionPlan plan, string artifactHash)
        => new(
            1,
            "delegate-cache-key",
            plan.ModuleHash,
            plan.PlanHash,
            plan.PolicyHash,
            plan.BindingManifestHash,
            "delegate-runtime",
            "delegate-compiler",
            "delegate-verifier",
            "1.0.0",
            "net10.0",
            ["dynamic-method"],
            artifactHash,
            DateTimeOffset.UtcNow);

    private sealed class FailingCompiler : ISandboxCompiler
    {
        public int Calls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("compiler must not be called");
        }
    }

    private sealed class DelegateCompiler : ISandboxCompiler
    {
        public int Calls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new CompiledArtifact(
                [],
                "delegate-artifact",
                Manifest(plan, "delegate-artifact"),
                new VerificationResult(true, [], "delegate-artifact", "delegate-verifier", DateTimeOffset.UtcNow),
                (_, _) => SandboxValue.FromInt32(123),
                CompiledRuntimeFormKind.DynamicMethod));
        }
    }

    private sealed class ThrowingDelegateCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new CompiledArtifact(
                [],
                "throwing-artifact",
                Manifest(plan, "throwing-artifact"),
                new VerificationResult(true, [], "throwing-artifact", "delegate-verifier", DateTimeOffset.UtcNow),
                (_, _) => throw new InvalidOperationException("compiled delegate failed"),
                CompiledRuntimeFormKind.DynamicMethod));
    }

    private sealed class SandboxFailureCompiler(SandboxErrorCode code) : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => throw new SandboxRuntimeException(new SandboxError(code, "compiler failed"));
    }

    private sealed class UnexpectedFailureCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("compiler failed unexpectedly");
    }
}
