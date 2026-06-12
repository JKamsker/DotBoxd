using SafeIR;
using SafeIR.Compiler;
using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class CompiledFallbackAuditTests
{
    [Fact]
    public async Task Compiled_mode_without_compiler_audits_interpreter_fallback()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Contains(result.AuditEvents, IsFallback(SandboxErrorCode.ValidationError));
    }

    [Fact]
    public async Task Compiled_verifier_failure_audits_interpreter_fallback_reason()
    {
        var host = HostWithCompiler(new VerifierFailureCompiler());
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Contains(result.AuditEvents, IsFallback(SandboxErrorCode.VerifierFailure));
    }

    [Fact]
    public async Task Compiled_verifier_failure_without_fallback_emits_verifier_audit()
    {
        var host = HostWithCompiler(new VerifierFailureCompiler());
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "VerifierFailure" && e.ErrorCode == SandboxErrorCode.VerifierFailure);
    }

    private static async Task<SandboxExecutionResult> ExecuteCompiledAsync(SandboxHost host, ExecutionPlan plan)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled });

    private static Predicate<SandboxAuditEvent> IsFallback(SandboxErrorCode code)
        => e => e.Kind == "ExecutionFallback" &&
                e.ErrorCode == code &&
                e.Message?.Contains("fell back to interpreted mode", StringComparison.Ordinal) == true;

    private static SandboxHost HostWithCompiler(ISandboxCompiler compiler)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

    private sealed class VerifierFailureCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.VerifierFailure,
                "compiled artifact failed verification"));
    }
}
