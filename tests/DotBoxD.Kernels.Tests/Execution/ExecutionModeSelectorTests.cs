using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Execution;

public sealed class ExecutionModeSelectorTests
{
    [Fact]
    public async Task Auto_mode_uses_configured_execution_mode_selector_after_first_run()
    {
        var selector = new RecordingSelector(ExecutionModeDecision.Interpreted);
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new FailingCompiler());
            builder.UseExecutionModeSelector(selector);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
        var options = new SandboxExecutionOptions { Mode = ExecutionMode.Auto, AutoCompileThreshold = 1 };

        var first = await host.ExecuteAsync(plan, "main", input, options);
        var second = await host.ExecuteAsync(plan, "main", input, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, first.ActualMode);
        Assert.Equal(ExecutionMode.Interpreted, second.ActualMode);
        Assert.True(first.ExecutionDispatched);
        Assert.True(second.ExecutionDispatched);
        Assert.Equal(1, selector.Calls);
        var hotness = selector.LastHotness!;
        Assert.Equal(plan.PlanHash, hotness.PlanHash);
        Assert.Equal("main", hotness.Entrypoint);
        Assert.Equal(2, hotness.RunCount);
        Assert.Equal(1, hotness.CompletedRunCount);
        Assert.Equal(first.ResourceUsage.FuelUsed, hotness.AverageFuelUsed);
        Assert.NotNull(hotness.LastRunAt);
        Assert.Equal(0, hotness.CompileFailures);
        Assert.Null(hotness.LastCompiledArtifactHash);
    }

    [Fact]
    public async Task Auto_mode_rejects_invalid_selector_decision_without_compiler_dispatch()
    {
        var selector = new RecordingSelector(new ExecutionModeDecision((ExecutionMode)123));
        var compiler = new FailingCompiler();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
            builder.UseExecutionModeSelector(selector);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
        var options = new SandboxExecutionOptions { Mode = ExecutionMode.Auto, AutoCompileThreshold = 1 };

        var first = await host.ExecuteAsync(plan, "main", input, options);
        var second = await host.ExecuteAsync(plan, "main", input, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.False(second.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, second.Error!.Code);
        Assert.Equal(ExecutionMode.Auto, second.ActualMode);
        Assert.False(second.ExecutionDispatched);
        Assert.Equal(1, selector.Calls);
        Assert.Equal(0, compiler.Calls);
        Assert.Contains(second.AuditEvents, e => e.Kind == "InvalidExecutionOptions");
    }

    [Fact]
    public async Task Auto_mode_hotness_tracks_prior_compile_failures()
    {
        var selector = new RecordingSelector(ExecutionModeDecision.Compiled);
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new UnexpectedFailureCompiler());
            builder.UseExecutionModeSelector(selector);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Auto,
            AutoCompileThreshold = 1,
            AllowFallbackToInterpreter = false
        };

        var first = await host.ExecuteAsync(plan, "main", input, options);
        var second = await host.ExecuteAsync(plan, "main", input, options);
        var third = await host.ExecuteAsync(plan, "main", input, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.False(second.Succeeded);
        Assert.False(third.Succeeded);
        Assert.Equal(2, selector.Calls);
        Assert.Equal(0, selector.HotnessSnapshots[0].CompileFailures);
        Assert.Equal(1, selector.HotnessSnapshots[1].CompileFailures);
        Assert.Equal(2, selector.HotnessSnapshots[1].CompletedRunCount);
    }

    private sealed class RecordingSelector(ExecutionModeDecision decision) : IExecutionModeSelector
    {
        public int Calls { get; private set; }
        public ModuleHotnessStats? LastHotness { get; private set; }
        public List<ModuleHotnessStats> HotnessSnapshots { get; } = [];

        public ExecutionModeDecision Choose(
            ExecutionPlan plan,
            SandboxExecutionOptions options,
            ModuleHotnessStats hotness,
            CompiledCacheStatus cacheStatus)
        {
            Calls++;
            LastHotness = hotness;
            HotnessSnapshots.Add(hotness);
            Assert.Equal(CompiledCacheStatus.None, cacheStatus);
            return decision;
        }
    }

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
