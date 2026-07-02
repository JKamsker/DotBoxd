using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

internal static class WorkerAuditValidationTestSupport
{
    public static SandboxHost Host(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    public static SandboxHost LogHost(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    public static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await PrepareAsync(host, module);
    }

    public static ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host, SandboxModule module)
        => host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

    public static ValueTask<SandboxExecutionResult> ExecuteAsync(SandboxHost host, ExecutionPlan plan)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

    public static SandboxModule MathBindingModule()
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

    public static Dictionary<string, string> BindingFields(ExecutionPlan plan, string resourceKind)
        => new(StringComparer.Ordinal)
        {
            ["resourceKind"] = resourceKind,
            ["durationMs"] = "0",
            ["moduleHash"] = plan.ModuleHash,
            ["policyHash"] = plan.PolicyHash
        };

    public static string LogJson()
        => """
        {
          "id": "worker-audit-validation-log",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "worker ok" }] } }
              ]
            }
          ]
        }
        """;
}

internal sealed class RecordingWorker(Action<SandboxExecutionOptions> onOptions) : ISandboxWorkerClient
{
    public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onOptions(options);
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var audit = new InMemoryAuditSink();

        if (!options.SuppressSuccessfulRunSummaryAudit)
        {
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: new Dictionary<string, string>(
                    RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None"),
                    StringComparer.Ordinal)));
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

internal sealed class AuditForgingWorker(
    Func<ExecutionPlan, SandboxRunId, SandboxAuditEvent> forgeAuditEvent,
    bool AddSummaryExtraField = false,
    Action<Dictionary<string, string>>? MutateSummaryFields = null,
    Action<List<SandboxAuditEvent>>? MutateAuditEvents = null,
    SandboxValue? Value = null) : ISandboxWorkerClient
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
        MutateSummaryFields?.Invoke(summaryFields);

        audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            DateTimeOffset.UtcNow,
            true,
            ResourceId: $"module:{plan.ModuleHash}",
            Fields: summaryFields));
        audit.Write(forgeAuditEvent(plan, runId));
        var auditEvents = audit.Events.ToList();
        MutateAuditEvents?.Invoke(auditEvents);

        return ValueTask.FromResult(new SandboxExecutionResult
        {
            Succeeded = true,
            Value = Value ?? SandboxValue.FromInt32(35),
            ResourceUsage = budget.Snapshot(),
            AuditEvents = auditEvents,
            ActualMode = ExecutionMode.Interpreted,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        });
    }
}
