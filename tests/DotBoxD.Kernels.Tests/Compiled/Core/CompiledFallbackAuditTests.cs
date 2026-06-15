using DotBoxD.Kernels;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Hosting;

namespace DotBoxD.Kernels.Tests;

public sealed class CompiledFallbackAuditTests
{
    [Fact]
    public async Task Compiled_mode_without_compiler_audits_interpreter_fallback()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
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
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "VerifierFailure" &&
            !e.Success &&
            e.ErrorCode == SandboxErrorCode.VerifierFailure);
        Assert.Contains(result.AuditEvents, IsFallback(SandboxErrorCode.VerifierFailure));
    }

    [Fact]
    public async Task Compiled_fallback_audit_events_are_sequenced()
    {
        var host = HostWithCompiler(new VerifierFailureCompiler());
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteCompiledAsync(host, plan);

        Assert.All(result.AuditEvents, e => Assert.True(e.SequenceNumber > 0));
        Assert.Equal(
            result.AuditEvents.Select(e => e.SequenceNumber).Order().ToArray(),
            result.AuditEvents.Select(e => e.SequenceNumber).ToArray());
    }

    [Fact]
    public async Task Compiled_verifier_failure_without_fallback_emits_verifier_audit()
    {
        var host = HostWithCompiler(new VerifierFailureCompiler());
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "VerifierFailure" && e.ErrorCode == SandboxErrorCode.VerifierFailure);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "RunSummary" && !e.Success && e.ErrorCode == SandboxErrorCode.VerifierFailure);
    }

    [Fact]
    public async Task Compiled_effectful_binding_runs_without_interpreter_fallback()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PrepareLogPlanAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "ExecutionFallback");
        Assert.Contains(result.AuditEvents, e => e.Kind == "SandboxLog" && e.BindingId == "log.info" && e.Success);
    }

    [Fact]
    public async Task Compiled_effectful_binding_runs_when_fallback_disabled()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PrepareLogPlanAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "CompiledExecutionFailed");
        Assert.Contains(result.AuditEvents, e => e.Kind == "SandboxLog" && e.BindingId == "log.info" && e.Success);
    }

    private static async Task<SandboxExecutionResult> ExecuteCompiledAsync(SandboxHost host, ExecutionPlan plan)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled });

    private static async Task<ExecutionPlan> PrepareLogPlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync("""
        {
          "id": "compiled-effectful-log",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [{ "op": "return", "value": { "call": "log.info", "args": [{ "string": "hello" }] } }]
            }
          ]
        }
        """);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().GrantLogging().WithFuel(1_000).Build());
    }

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
