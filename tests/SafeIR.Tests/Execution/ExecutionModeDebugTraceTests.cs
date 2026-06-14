using SafeIR.Compiler;
using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class ExecutionModeDebugTraceTests
{
    [Fact]
    public async Task Auto_mode_with_debug_trace_stays_interpreted_and_skips_compiler()
    {
        var compiler = new FailingCompiler();
        var host = HostWithCompiler(compiler);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Auto,
            EnableDebugTrace = true,
            AutoCompileThreshold = 1
        };
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

        var first = await host.ExecuteAsync(plan, "main", input, options);
        var second = await host.ExecuteAsync(plan, "main", input, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, first.ActualMode);
        Assert.Equal(ExecutionMode.Interpreted, second.ActualMode);
        Assert.Equal(0, compiler.Calls);
        Assert.Contains(second.AuditEvents, e => e.Kind == "DebugTrace");
    }

    [Fact]
    public async Task Compiled_mode_with_debug_trace_falls_back_to_interpreter_when_allowed()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, EnableDebugTrace = true });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Contains(result.AuditEvents, e => e.Kind == "ExecutionFallback");
        Assert.Contains(result.AuditEvents, e => e.Kind == "DebugTrace");
        Assert.Null(result.ArtifactHash);
    }

    [Fact]
    public async Task Compiled_mode_with_debug_trace_fails_when_fallback_disabled()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                EnableDebugTrace = true,
                AllowFallbackToInterpreter = false
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Contains("debug tracing", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Contains(result.AuditEvents, e => e.Kind == "CompilerUnavailable");
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "DebugTrace");
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
}
