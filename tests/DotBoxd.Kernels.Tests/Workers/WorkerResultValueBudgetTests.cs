using DotBoxd.Hosting;

namespace DotBoxd.Kernels.Tests;

public sealed class WorkerResultValueBudgetTests
{
    [Fact]
    public async Task Worker_success_string_exceeding_result_budget_is_rejected()
    {
        var worker = new ResultValueWorker(SandboxValue.FromString("this is too long"));
        var host = Host(worker);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxStringLength(4)
            .WithMaxTotalStringBytes(8)
            .Build();
        var plan = await PrepareStringAsync(host, policy);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Worker_success_list_exceeding_result_budget_is_rejected()
    {
        var value = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
            SandboxType.I32);
        var worker = new ResultValueWorker(value);
        var host = Host(worker);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxListLength(1)
            .WithMaxTotalCollectionElements(1)
            .Build();
        var plan = await PrepareListAsync(host, policy);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Worker_success_with_underreported_result_shape_usage_is_rejected()
    {
        var worker = new ResultValueWorker(SandboxValue.FromString("ok"));
        var host = Host(worker);
        var plan = await PrepareStringAsync(
            host,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

    private static SandboxHost Host(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static async ValueTask<ExecutionPlan> PrepareStringAsync(
        SandboxHost host,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync("""
        {
          "id": "worker-string-result",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [{ "op": "return", "value": { "string": "ok" } }]
            }
          ]
        }
        """);
        return await host.PrepareAsync(module, policy);
    }

    private static async ValueTask<ExecutionPlan> PrepareListAsync(
        SandboxHost host,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync("""
        {
          "id": "worker-list-result",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.empty",
                    "genericType": "I32",
                    "args": []
                  }
                }
              ]
            }
          ]
        }
        """);
        return await host.PrepareAsync(module, policy);
    }

    private sealed class ResultValueWorker(SandboxValue resultValue) : ISandboxWorkerClient
    {
        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            var budget = new ResourceMeter(plan.Budget);
            var runId = options.RunId ?? SandboxRunId.New();
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                Success: true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: RunSummaryAuditFields.Create(
                    plan,
                    budget,
                    ExecutionMode.Interpreted,
                    "None",
                    executionDispatched: true)));
            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = resultValue,
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
