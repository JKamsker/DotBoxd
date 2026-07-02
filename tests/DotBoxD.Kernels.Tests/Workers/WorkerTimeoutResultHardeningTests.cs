using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerTimeoutResultHardeningTests
{
    [Fact]
    public async Task Worker_success_returned_after_host_timeout_is_rejected()
    {
        var worker = new TimeoutSwallowingWorker();
        using var host = Host(worker);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithWallTime(TimeSpan.FromMilliseconds(20))
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
        Assert.Equal(1, worker.Calls);
    }

    [Fact]
    public async Task Worker_wall_time_timeout_does_not_require_worker_cooperation()
    {
        var worker = new NonCooperativeTimeoutWorker();
        using var host = Host(worker);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithWallTime(TimeSpan.FromMilliseconds(20))
                .Build());

        var resultTask = host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
                new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess })
            .AsTask();

        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        await worker.TimeoutObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(worker.ObservedTimeoutCancellation);
    }

    [Fact]
    public async Task Worker_execution_observes_caller_cancellation()
    {
        var worker = new CallerCancellationWorker();
        using var host = Host(worker);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithWallTime(TimeSpan.FromSeconds(30))
                .Build());

        using var caller = new CancellationTokenSource();
        var resultTask = host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
                new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess },
                caller.Token)
            .AsTask();

        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await caller.CancelAsync();

        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.True(worker.ObservedCancellation);
    }

    private static SandboxHost Host(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private sealed class TimeoutSwallowingWorker : ISandboxWorkerClient
    {
        public int Calls { get; private set; }

        public async ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Malicious or buggy worker observed the host-side timeout but still returns success.
            }

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

            return new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt32(35),
                ResourceUsage = budget.Snapshot(),
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            };
        }
    }

    private sealed class NonCooperativeTimeoutWorker : ISandboxWorkerClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TimeoutObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObservedTimeoutCancellation { get; private set; }

        public async ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObservedTimeoutCancellation = true;
                TimeoutObserved.SetResult();
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("The non-cooperative worker should not return.");
        }
    }

    private sealed class CallerCancellationWorker : ISandboxWorkerClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObservedCancellation { get; private set; }

        public async ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObservedCancellation = true;
                throw;
            }

            throw new InvalidOperationException("The caller cancellation test should cancel the worker.");
        }
    }
}
