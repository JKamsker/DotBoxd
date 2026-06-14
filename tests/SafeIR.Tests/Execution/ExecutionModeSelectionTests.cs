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
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(35, ((I32Value)result.Value!).Value);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.Null(result.ArtifactHash);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Auto_mode_uses_interpreter_below_hotness_threshold()
    {
        var compiler = new FailingCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
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
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
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
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
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
    public async Task Auto_mode_keeps_effectful_entrypoint_interpreted_after_hotness_threshold()
    {
        var compiler = new FailingCompiler();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "auto-effectful",
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
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().GrantLogging().WithFuel(1_000).Build());
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Auto,
            AutoCompileThreshold = 2,
            AllowFallbackToInterpreter = false
        };

        var first = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
        var second = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, first.ActualMode);
        Assert.Equal(ExecutionMode.Interpreted, second.ActualMode);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Compiled_mode_without_compiler_falls_back_when_allowed()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
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
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.Null(result.ArtifactHash);
        Assert.Contains(result.AuditEvents, e => e.Kind == "CompilerUnavailable");
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.ValidationError, summary.ErrorCode);
        Assert.Equal("Compiled", summary.Fields!["mode"]);
        Assert.Equal("False", summary.Fields["executionDispatched"]);
    }

    [Fact]
    public async Task Invalid_execution_mode_fails_without_compiler_dispatch()
    {
        var compiler = new FailingCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
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
        Assert.False(result.ExecutionDispatched);
        Assert.Equal(0, compiler.Calls);
        Assert.Contains(result.AuditEvents, e => e.Kind == "InvalidExecutionOptions");
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "CompilerUnavailable");
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.Equal("123", summary.Fields!["mode"]);
        Assert.Equal("False", summary.Fields["executionDispatched"]);
    }

    [Fact]
    public async Task Compiled_mode_rejects_dynamic_method_artifact_before_delegate_runs()
    {
        var compiler = new DynamicDelegateCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.Null(result.ArtifactHash);
        Assert.Equal(1, compiler.Calls);
        Assert.False(compiler.DelegateExecuted);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "RunSummary" && !e.Success && e.ErrorCode == SandboxErrorCode.ValidationError);
    }

    [Fact]
    public async Task Compiled_mode_ignores_untrusted_loaded_assembly_delegate()
    {
        var compiler = new TamperedExecutionDelegateCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
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
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.VerifierFailure, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.Null(result.ArtifactHash);
        Assert.Contains(result.AuditEvents, e => e.Kind == "CompiledExecutionFailed");
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "RunSummary" && !e.Success && e.ErrorCode == SandboxErrorCode.VerifierFailure);
    }

    [Fact]
    public async Task Compiled_mode_unexpected_compiler_exception_returns_host_failure()
    {
        var host = HostWithCompiler(new UnexpectedFailureCompiler());
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
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
}
