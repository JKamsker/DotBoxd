using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class WorkerIsolationTests
{
    [Fact]
    public async Task Worker_process_isolation_request_fails_closed_when_unconfigured()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Auto,
                Isolation = SandboxIsolation.WorkerProcess
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Null(result.ArtifactHash);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationUnavailable");
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.PolicyDenied, summary.ErrorCode);
        Assert.Equal("Auto", summary.Fields!["mode"]);
    }

    [Fact]
    public async Task Worker_process_isolation_rejects_incomplete_worker_profile_without_invoking_client()
    {
        var worker = new CapturingWorker
        {
            ResultMode = ExecutionMode.Compiled,
            ResultArtifactHash = new string('a', 64)
        };
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, new SandboxWorkerProfile(
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
        Assert.Equal(0, worker.Calls);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "WorkerIsolationUnavailable");
        Assert.Equal("False", audit.Fields!["outOfProcess"]);
    }

    [Fact]
    public async Task Worker_process_isolation_delegates_to_hardened_worker_client()
    {
        var observed = new List<SandboxAuditEvent>();
        var worker = new CapturingWorker();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var plan = await PrepareAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                Isolation = SandboxIsolation.WorkerProcess
            });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1, worker.Calls);
        Assert.Equal(SandboxIsolation.InProcess, worker.Options!.Isolation);
        Assert.Equal(ExecutionMode.Compiled, worker.Options.Mode);
        Assert.Equal(result.AuditEvents, observed);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerExecution");
        Assert.All(result.AuditEvents, e => Assert.True(e.SequenceNumber > 0));
    }

    [Fact]
    public async Task Worker_process_isolation_rejects_worker_result_for_wrong_plan()
    {
        var worker = new CapturingWorker { ReturnWrongPlanIdentity = true };
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
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

    [Fact]
    public async Task Worker_process_isolation_rejects_worker_result_with_auto_actual_mode()
    {
        var worker = new CapturingWorker { ResultMode = ExecutionMode.Auto };
        var host = HostWithWorker(worker);
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

    [Fact]
    public async Task Worker_process_isolation_rejects_compiled_success_without_artifact_hash()
    {
        var worker = new CapturingWorker { ResultMode = ExecutionMode.Compiled };
        var host = HostWithWorker(worker);
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

    [Fact]
    public async Task Worker_process_isolation_rejects_interpreted_result_when_compiled_fallback_is_disabled()
    {
        var worker = new CapturingWorker { ResultMode = ExecutionMode.Interpreted };
        var host = HostWithWorker(worker);
        var plan = await PrepareAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                AllowFallbackToInterpreter = false,
                Isolation = SandboxIsolation.WorkerProcess
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_process_isolation_rejects_compiled_result_for_interpreted_request()
    {
        var worker = new CapturingWorker
        {
            ResultMode = ExecutionMode.Compiled,
            ResultArtifactHash = new string('b', 64)
        };
        var host = HostWithWorker(worker);
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

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_process_isolation_rejects_success_with_wrong_return_type()
    {
        var worker = new CapturingWorker { ResultValue = SandboxValue.Unit };
        var host = HostWithWorker(worker);
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

    [Fact]
    public async Task Worker_process_isolation_rejects_success_with_error()
    {
        var worker = new CapturingWorker
        {
            ResultError = new SandboxError(SandboxErrorCode.HostFailure, "unexpected worker error")
        };
        var host = HostWithWorker(worker);
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

    [Fact]
    public async Task Worker_process_isolation_rejects_failure_without_error()
    {
        var worker = new CapturingWorker { Succeeded = false };
        var host = HostWithWorker(worker);
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

    private static SandboxHost HostWithWorker(CapturingWorker worker)
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

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    private sealed class CapturingWorker : ISandboxWorkerClient
    {
        public int Calls { get; private set; }
        public SandboxExecutionOptions? Options { get; private set; }
        public bool ReturnWrongPlanIdentity { get; init; }
        public ExecutionMode ResultMode { get; init; } = ExecutionMode.Interpreted;
        public string? ResultArtifactHash { get; init; }
        public bool Succeeded { get; init; } = true;
        public SandboxValue? ResultValue { get; init; } = SandboxValue.FromInt32(35);
        public SandboxError? ResultError { get; init; }

        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Options = options;
            var runId = options.RunId ?? SandboxRunId.New();
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "WorkerExecution",
                DateTimeOffset.UtcNow,
                Succeeded,
                ResourceId: $"module:{plan.ModuleHash}",
                ErrorCode: ResultError?.Code,
                Message: entrypoint));
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                Succeeded,
                ResourceId: $"module:{plan.ModuleHash}",
                ErrorCode: ResultError?.Code,
                Message: "worker execution completed"));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = Succeeded,
                Value = Succeeded ? ResultValue : null,
                Error = ResultError,
                ResourceUsage = new ResourceMeter(plan.Budget).Snapshot(),
                AuditEvents = audit.Events,
                ActualMode = ResultMode,
                ModuleHash = ReturnWrongPlanIdentity ? "wrong-module" : plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash,
                ArtifactHash = ResultArtifactHash
            });
        }
    }
}
