using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Hosting.Regression;

/// <summary>
/// Regression coverage for API-0012: the worker-process feature now ships a usable, in-box
/// reference worker client (<see cref="SandboxHostWorkerClient"/>) instead of relying solely on
/// private test doubles. These tests prove the shipped adapter drives a real worker-side host
/// across the worker boundary and still fails closed on a misprofiled or divergent worker.
/// </summary>
public sealed class Fix_API_0012_Tests
{
    [Fact]
    public async Task Shipped_worker_client_executes_pure_module_across_worker_boundary()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(
                new SandboxHostWorkerClient(WorkerHostFactory),
                SandboxWorkerProfile.HardenedOutOfProcess);
        });
        var plan = await PrepareAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                Isolation = SandboxIsolation.WorkerProcess
            });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(result.ExecutionDispatched);
        Assert.Equal(SandboxValue.FromInt32(35), result.Value);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Contains(result.AuditEvents, e => e.Kind == "RunSummary");
    }

    [Fact]
    public async Task Shipped_worker_client_reuses_worker_host_across_requests()
    {
        var factoryCalls = 0;
        using var worker = new SandboxHostWorkerClient(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return WorkerHostFactory();
        });
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });
        var plan = await PrepareAsync(host);
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            Isolation = SandboxIsolation.WorkerProcess
        };

        var first = await host.ExecuteAsync(plan, "main", Input(), options);
        var second = await host.ExecuteAsync(plan, "main", Input(), options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(1, Volatile.Read(ref factoryCalls));
    }

    [Fact]
    public async Task Shipped_worker_client_fails_closed_when_profile_is_incomplete()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(
                new SandboxHostWorkerClient(WorkerHostFactory),
                new SandboxWorkerProfile(
                    OutOfProcess: false,
                    SecretsIsolated: true,
                    ResourceLimitsConfigured: true));
        });
        var plan = await PrepareAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.False(result.ExecutionDispatched);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationUnavailable");
    }

    [Fact]
    public async Task Shipped_worker_client_fails_closed_when_worker_bindings_diverge()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(
                new SandboxHostWorkerClient(DivergentWorkerHostFactory),
                SandboxWorkerProfile.HardenedOutOfProcess);
        });
        var plan = await PrepareAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost WorkerHostFactory()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

    private static SandboxHost DivergentWorkerHostFactory()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
}
