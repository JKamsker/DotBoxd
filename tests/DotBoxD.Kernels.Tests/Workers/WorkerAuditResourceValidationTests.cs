using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerAuditResourceValidationTests
{
    [Fact]
    public async Task Worker_rejects_log_audit_when_usage_underreports_host_call_and_log_event()
    {
        var worker = new ForgedLogWorker(HostCalls: 0, LogEvents: 0, Message: "worker ok");
        using var host = LogHost(worker);
        var plan = await PrepareLogPlanAsync(host, SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(1_000)
            .WithMaxLogEvents(10)
            .Build());

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_rejects_log_audit_message_that_exceeds_policy_length()
    {
        var worker = new ForgedLogWorker(HostCalls: 1, LogEvents: 1, Message: "too-long");
        using var host = LogHost(worker);
        var plan = await PrepareLogPlanAsync(host, SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(1_000)
            .WithMaxLogEvents(10)
            .WithMaxLogMessageLength(4)
            .Build());

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost LogHost(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static async Task<ExecutionPlan> PrepareLogPlanAsync(SandboxHost host, SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(LogJson());
        return await host.PrepareAsync(module, policy);
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(SandboxHost host, ExecutionPlan plan)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

    private static SandboxResourceUsage Usage(ExecutionPlan plan, int hostCalls, int logEvents)
        => new(
            FuelUsed: 0,
            MaxFuel: plan.Budget.MaxFuel,
            LoopIterations: 0,
            AllocatedBytes: 0,
            HostCalls: hostCalls,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: logEvents,
            CollectionElements: 0,
            StringBytes: 0);

    private static Dictionary<string, string> SummaryFields(
        ExecutionPlan plan,
        int hostCalls,
        int logEvents)
    {
        var fields = new Dictionary<string, string>(
            RunSummaryAuditFields.Create(plan, new ResourceMeter(plan.Budget), ExecutionMode.Interpreted, "None"),
            StringComparer.Ordinal);
        fields["hostCalls"] = hostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields["logEvents"] = logEvents.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return fields;
    }

    private static Dictionary<string, string> BindingFields(ExecutionPlan plan)
        => new(StringComparer.Ordinal)
        {
            ["resourceKind"] = "log",
            ["durationMs"] = "0",
            ["moduleHash"] = plan.ModuleHash,
            ["policyHash"] = plan.PolicyHash
        };

    private static string LogJson()
        => """
        {
          "id": "worker-audit-resource-validation-log",
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

    private sealed class ForgedLogWorker(int HostCalls, int LogEvents, string Message) : ISandboxWorkerClient
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
            var usage = Usage(plan, HostCalls, LogEvents);
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan, HostCalls, LogEvents)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "SandboxLog",
                DateTimeOffset.UtcNow,
                true,
                BindingId: "log.info",
                CapabilityId: "log.write",
                Effect: SandboxEffect.Audit,
                ResourceId: "log:info",
                Message: Message,
                Fields: BindingFields(plan)));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.Unit,
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }
}
