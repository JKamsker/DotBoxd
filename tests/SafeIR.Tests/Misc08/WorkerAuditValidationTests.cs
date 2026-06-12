using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class WorkerAuditValidationTests
{
    [Fact]
    public async Task Worker_result_with_undefined_non_summary_error_code_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "WorkerExecution",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: (SandboxErrorCode)123456));
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_result_with_forged_binding_audit_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "BindingCall",
            DateTimeOffset.UtcNow,
            true,
            BindingId: "math.abs",
            Effect: SandboxEffect.Cpu,
            ResourceId: "binding:math.abs",
            Fields: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resourceKind"] = "binding",
                ["durationMs"] = "0",
                ["moduleHash"] = plan.ModuleHash,
                ["policyHash"] = plan.PolicyHash
            }));
        var host = Host(worker);
        var plan = await PrepareAsync(host, MathBindingModule());

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.DoesNotContain(result.AuditEvents, e => e.BindingId == "math.abs");
    }

    [Fact]
    public async Task Worker_result_with_unknown_non_summary_audit_kind_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "ForgedEvidence",
            DateTimeOffset.UtcNow,
            true,
            ResourceId: $"module:{plan.ModuleHash}"));
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_result_with_forged_audit_timestamp_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "WorkerExecution",
            DateTimeOffset.UtcNow.AddYears(10),
            true,
            ResourceId: $"module:{plan.ModuleHash}"));
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_result_with_extra_run_summary_field_is_rejected()
    {
        var worker = new AuditForgingWorker(
            (plan, runId) => new SandboxAuditEvent(
                runId,
                "WorkerExecution",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}"),
            AddSummaryExtraField: true);
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost Host(AuditForgingWorker worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await PrepareAsync(host, module);
    }

    private static ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host, SandboxModule module)
        => host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(SandboxHost host, ExecutionPlan plan)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

    private static SandboxModule MathBindingModule()
        => new(
            "worker-audit-validation-math",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.I32,
                    [
                        new ReturnStatement(
                            new CallExpression(
                                "math.abs",
                                [new LiteralExpression(SandboxValue.FromInt32(-1), new SourceSpan(0, 0))],
                                null,
                                new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private sealed class AuditForgingWorker(
        Func<ExecutionPlan, SandboxRunId, SandboxAuditEvent> forgeAuditEvent,
        bool AddSummaryExtraField = false) : ISandboxWorkerClient
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
            var summaryFields = new Dictionary<string, string>(
                RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None"),
                StringComparer.Ordinal);
            if (AddSummaryExtraField)
            {
                summaryFields["forged"] = "field";
            }

            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: summaryFields));
            audit.Write(forgeAuditEvent(plan, runId));

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
