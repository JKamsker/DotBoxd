using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class WorkerEnvelopeValidationTests
{
    private static readonly string ValidCacheKey = new('f', 64);

    [Theory]
    [InlineData("unsafe\rmessage", null)]
    [InlineData("password token leaked", null)]
    [InlineData("defined error", "bad\rdiagnostic")]
    [InlineData("defined error", "unsafe:diagnostic")]
    public async Task Worker_failure_with_unsafe_error_text_is_rejected(string safeMessage, string? diagnosticId)
    {
        var worker = new EnvelopeWorker
        {
            Error = new SandboxError(SandboxErrorCode.InvalidInput, safeMessage, diagnosticId)
        };
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Theory]
    [InlineData("maxAllocatedBytes", "999999")]
    [InlineData("maxHostCalls", "999")]
    public async Task Worker_run_summary_with_forged_budget_ceiling_is_rejected(string fieldName, string fieldValue)
    {
        var worker = new EnvelopeWorker
        {
            ForgedSummaryField = fieldName,
            ForgedSummaryValue = fieldValue
        };
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Theory]
    [InlineData("not-an-artifact-hash", null, null)]
    [InlineData(null, "artifactHash", "not-an-artifact-hash")]
    public async Task Failed_compiled_worker_result_rejects_malformed_artifact_hash(
        string? artifactHash,
        string? forgedField,
        string? forgedValue)
    {
        var worker = new EnvelopeWorker
        {
            Error = new SandboxError(SandboxErrorCode.InvalidInput, "defined worker error"),
            ResultMode = ExecutionMode.Compiled,
            ArtifactHash = artifactHash,
            ForgedSummaryField = forgedField,
            ForgedSummaryValue = forgedValue
        };
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost Host(EnvelopeWorker worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxAllocatedBytes(512)
            .WithMaxHostCalls(2)
            .Build();
        return await host.PrepareAsync(module, policy);
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(SandboxHost host, ExecutionPlan plan)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

    private sealed class EnvelopeWorker : ISandboxWorkerClient
    {
        public SandboxError? Error { get; init; }
        public string? ForgedSummaryField { get; init; }
        public string? ForgedSummaryValue { get; init; }
        public ExecutionMode ResultMode { get; init; } = ExecutionMode.Interpreted;
        public string? ArtifactHash { get; init; }

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
            var fields = new Dictionary<string, string>(
                RunSummaryAuditFields.Create(
                    plan,
                    budget,
                    ResultMode,
                    "None",
                    ResultMode == ExecutionMode.Compiled && ArtifactHash is not null ? "LoadedAssembly" : null,
                    ResultMode == ExecutionMode.Compiled && ArtifactHash is not null ? ValidCacheKey : null,
                    ResultMode == ExecutionMode.Compiled ? ArtifactHash : null),
                StringComparer.Ordinal);

            if (ForgedSummaryField is not null && ForgedSummaryValue is not null)
            {
                fields[ForgedSummaryField] = ForgedSummaryValue;
            }

            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                Error is null,
                ResourceId: $"module:{plan.ModuleHash}",
                ErrorCode: Error?.Code,
                Fields: fields));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = Error is null,
                Value = Error is null ? SandboxValue.FromInt32(35) : null,
                Error = Error,
                ResourceUsage = budget.Snapshot(),
                AuditEvents = audit.Events,
                ActualMode = ResultMode,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash,
                ArtifactHash = ArtifactHash
            });
        }
    }
}
