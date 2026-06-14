using DotBoxd.Hosting;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for ALG-0016: worker-result audit validation now validates the audit
/// envelope in a single pass over <see cref="SandboxExecutionResult.AuditEvents"/> instead of
/// rescanning the list for the common run id, per-event validation, and the run summary while
/// allocating a summary array. These tests pin the observable boundary behavior that the
/// single-pass rewrite must preserve: a valid envelope is still accepted and published, and an
/// envelope carrying more than one <c>RunSummary</c> is still rejected fail-closed.
/// </summary>
public sealed class Fix_ALG_0016_Tests
{
    [Fact]
    public async Task Valid_worker_envelope_with_single_summary_is_accepted_and_published()
    {
        var worker = new SummaryControlWorker(extraSummary: false);
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(result.ExecutionDispatched);
        Assert.Equal(SandboxValue.FromInt32(35), result.Value);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.True(summary.Success);
        // Resequencing semantics are preserved on the accepted path.
        Assert.All(result.AuditEvents, e => Assert.True(e.SequenceNumber > 0));
    }

    [Fact]
    public async Task Worker_envelope_with_two_run_summaries_is_rejected_fail_closed()
    {
        var worker = new SummaryControlWorker(extraSummary: true);
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost Host(SummaryControlWorker worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(SandboxHost host, ExecutionPlan plan)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

    private sealed class SummaryControlWorker(bool extraSummary) : ISandboxWorkerClient
    {
        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = options.RunId ?? SandboxRunId.New();
            var budget = new ResourceMeter(plan.Budget);
            var audit = new InMemoryAuditSink();

            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None")));

            if (extraSummary)
            {
                audit.Write(new SandboxAuditEvent(
                    runId,
                    "RunSummary",
                    DateTimeOffset.UtcNow,
                    true,
                    ResourceId: $"module:{plan.ModuleHash}",
                    Fields: RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None")));
            }

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt32(35),
                ResourceUsage = budget.Snapshot(),
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }
}
