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
    public async Task Auto_mode_uses_interpreter_below_hotness_threshold()
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
    public async Task Auto_mode_promotes_to_compiled_after_hotness_threshold()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var options = new SandboxExecutionOptions { Mode = ExecutionMode.Auto, AutoCompileThreshold = 2 };
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

        var first = await host.ExecuteAsync(plan, "main", input, options);
        var second = await host.ExecuteAsync(plan, "main", input, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, first.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, second.ActualMode);
    }

    [Fact]
    public async Task Auto_mode_threshold_one_still_starts_interpreted()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var options = new SandboxExecutionOptions { Mode = ExecutionMode.Auto, AutoCompileThreshold = 1 };
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

        var first = await host.ExecuteAsync(plan, "main", input, options);
        var second = await host.ExecuteAsync(plan, "main", input, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, first.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, second.ActualMode);
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
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.ValidationError, summary.ErrorCode);
        Assert.Equal("Compiled", summary.Fields!["mode"]);
    }

    [Fact]
    public async Task Invalid_execution_mode_fails_without_compiler_dispatch()
    {
        var compiler = new FailingCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var invalidMode = (ExecutionMode)123;

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = invalidMode });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal(invalidMode, result.ActualMode);
        Assert.Equal(0, compiler.Calls);
        Assert.Contains(result.AuditEvents, e => e.Kind == "InvalidExecutionOptions");
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "CompilerUnavailable");
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.Equal("123", summary.Fields!["mode"]);
    }

    [Fact]
    public async Task Compiled_mode_rejects_dynamic_method_artifact_before_delegate_runs()
    {
        var compiler = new DynamicDelegateCompiler();
        var host = HostWithCompiler(compiler);
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
        Assert.Equal(1, compiler.Calls);
        Assert.False(compiler.DelegateExecuted);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "RunSummary" && !e.Success && e.ErrorCode == SandboxErrorCode.ValidationError);
    }

    [Fact]
    public async Task Compiled_mode_ignores_untrusted_loaded_assembly_delegate()
    {
        var compiler = new TamperedLoadedAssemblyCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(35, ((I32Value)result.Value!).Value);
        Assert.False(compiler.DelegateExecuted);
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
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "RunSummary" && !e.Success && e.ErrorCode == SandboxErrorCode.VerifierFailure);
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
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "RunSummary" && !e.Success && e.ErrorCode == SandboxErrorCode.HostFailure);
    }

    private static SandboxHost HostWithCompiler(ISandboxCompiler compiler)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

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

    private sealed class DynamicDelegateCompiler : ISandboxCompiler
    {
        public int Calls { get; private set; }
        public bool DelegateExecuted { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(CompiledArtifactTestFactory.DynamicMethod(
                plan,
                (_, _) =>
                {
                    DelegateExecuted = true;
                    return SandboxValue.FromInt32(123);
                },
                "delegate-artifact"));
        }
    }

    private sealed class TamperedLoadedAssemblyCompiler : ISandboxCompiler
    {
        private readonly ReflectionEmitSandboxCompiler _inner = new(new GeneratedAssemblyVerifier());

        public bool DelegateExecuted { get; private set; }

        public async ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            var artifact = await _inner.CompileAsync(plan, options, cancellationToken).ConfigureAwait(false);
            return artifact with
            {
                Entrypoint = (_, _) =>
                {
                    DelegateExecuted = true;
                    return SandboxValue.FromInt32(999);
                }
            };
        }
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
