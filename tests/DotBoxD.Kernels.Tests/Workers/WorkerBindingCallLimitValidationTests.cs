using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerBindingCallLimitValidationTests
{
    private const string BindingId = "test.limited";

    [Fact]
    public async Task Worker_rejects_audit_evidence_exceeding_binding_max_calls_per_run()
    {
        var worker = new ForgedLimitedBindingWorker();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(LimitedBinding());
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxHostCalls(10)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static BindingDescriptor LimitedBinding()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            new BindingCostModel(BaseFuel: 1, MaxCallsPerRun: 1),
            AuditLevel.PerCall,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static string ModuleJson()
        => """
        {
          "id": "worker-binding-call-limit-validation",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "return", "value": { "call": "test.limited", "args": [] } }
              ]
            }
          ]
        }
        """;

    private sealed class ForgedLimitedBindingWorker : ISandboxWorkerClient
    {
        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var usage = new SandboxResourceUsage(
                FuelUsed: 2,
                MaxFuel: plan.Budget.MaxFuel,
                LoopIterations: 0,
                AllocatedBytes: 0,
                HostCalls: 2,
                FileBytesRead: 0,
                FileBytesWritten: 0,
                NetworkBytesRead: 0,
                NetworkBytesWritten: 0,
                LogEvents: 0,
                CollectionElements: 0,
                StringBytes: 0);
            var runId = options.RunId ?? SandboxRunId.New();
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan, usage)));
            audit.Write(BindingAudit(plan, runId));
            audit.Write(BindingAudit(plan, runId));

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

        private static Dictionary<string, string> SummaryFields(
            ExecutionPlan plan,
            SandboxResourceUsage usage)
        {
            var fields = new Dictionary<string, string>(
                RunSummaryAuditFields.Create(
                    plan,
                    new ResourceMeter(plan.Budget),
                    ExecutionMode.Interpreted,
                    "None"),
                StringComparer.Ordinal);
            fields["fuelUsed"] = usage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields["hostCalls"] = usage.HostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return fields;
        }

        private static SandboxAuditEvent BindingAudit(ExecutionPlan plan, SandboxRunId runId)
            => new(
                runId,
                "BindingCall",
                DateTimeOffset.UtcNow,
                true,
                BindingId: BindingId,
                Effect: SandboxEffect.Cpu,
                ResourceId: "test:limited",
                Fields: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["resourceKind"] = "test",
                    ["durationMs"] = "0",
                    ["moduleHash"] = plan.ModuleHash,
                    ["policyHash"] = plan.PolicyHash
                });
    }
}
