using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Regression coverage for ALG-0014. Plugin execution telemetry used to rescan every
/// result's audit-event list twice per recorded execution: once with
/// <c>LastOrDefault(e =&gt; e.Kind == "RunSummary")</c> for the run-summary fields and again
/// with <c>FirstOrDefault(e =&gt; e.Kind == "ExecutionFallback")</c> for the fallback reason.
/// <see cref="PluginExecutionObserver.Record(string, ExecutionMode, SandboxExecutionResult)"/>
/// now walks the audit list a single time and
/// captures both markers in one pass. These tests pin the observable telemetry mapping that
/// the single-pass rewrite must preserve byte-for-byte: a successful interpreted run derives
/// its run-summary fields (cache status, runtime form, materialization) from the summary
/// event and reports no fallback, exactly as the two prior LINQ scans did.
/// </summary>
public sealed class Fix_ALG_0014_Tests
{
    [Fact]
    public async Task Successful_run_derives_run_summary_fields_and_reports_no_fallback()
    {
        var server = PluginAddendumTestPolicies.CreateServer(executionMode: ExecutionMode.Interpreted);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new DamageEventAdapter();
        var e = new DamageEvent("fire", 120, "player-1");

        Assert.True(await kernel.ShouldHandleAsync(adapter, e));
        await kernel.HandleAsync(adapter, e);

        // ShouldHandle then Handle each record one observation.
        Assert.Equal(2, kernel.ExecutionObservations.Count);

        var shouldHandle = kernel.ExecutionObservations[0];
        Assert.Equal("ShouldHandle", shouldHandle.Entrypoint);
        // Fields recovered from the last RunSummary audit event (single-pass extraction).
        Assert.Equal("None", shouldHandle.CacheStatus);
        Assert.Null(shouldHandle.RuntimeForm);
        Assert.Null(shouldHandle.CacheKey);
        Assert.Null(shouldHandle.MaterializationStatus);
        // No ExecutionFallback marker on the interpreted happy path.
        Assert.Null(shouldHandle.FallbackReason);

        var handle = kernel.LastExecution;
        Assert.NotNull(handle);
        Assert.Equal("Handle", handle.Entrypoint);
        Assert.True(handle.Succeeded);
        Assert.Equal(ExecutionMode.Interpreted, handle.ActualMode);
        Assert.Equal("None", handle.CacheStatus);
        Assert.Null(handle.FallbackReason);
        Assert.Null(handle.MaterializationStatus);
    }

    [Fact]
    public async Task Each_recorded_execution_appends_one_observation_with_its_own_summary()
    {
        var server = PluginAddendumTestPolicies.CreateServer(executionMode: ExecutionMode.Interpreted);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new DamageEventAdapter();
        var e = new DamageEvent("fire", 120, "player-1");

        await kernel.ShouldHandleAsync(adapter, e);
        await kernel.ShouldHandleAsync(adapter, e);
        await kernel.HandleAsync(adapter, e);

        Assert.Equal(3, kernel.ExecutionObservations.Count);
        // Telemetry extraction is per-result and never leaks fallback markers across runs.
        Assert.All(kernel.ExecutionObservations, observation =>
        {
            Assert.Equal(ExecutionMode.Interpreted, observation.RequestedMode);
            Assert.Equal(ExecutionMode.Interpreted, observation.ActualMode);
            Assert.Equal("None", observation.CacheStatus);
            Assert.Null(observation.FallbackReason);
        });
    }

    private sealed class DamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }
}
