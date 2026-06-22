using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Workers;

internal sealed class CapturingWorker : ISandboxWorkerClient
{
    private static readonly string ValidWorkerCacheKey = new('d', 64);

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
        var budget = new ResourceMeter(plan.Budget);
        audit.Write(new SandboxAuditEvent(
            runId,
            "WorkerExecution",
            DateTimeOffset.UtcNow,
            Succeeded,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: ResultError?.Code,
            Message: entrypoint));
        var runtimeForm = ResultMode == ExecutionMode.Compiled && !string.IsNullOrWhiteSpace(ResultArtifactHash)
            ? "LoadedAssembly"
            : null;
        var cacheKey = runtimeForm is not null ? ValidWorkerCacheKey : null;
        audit.Write(new SandboxAuditEvent(
            runId,
            "RunSummary",
            DateTimeOffset.UtcNow,
            Succeeded,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: ResultError?.Code,
            Message: "worker execution completed",
            Fields: RunSummaryAuditFields.Create(
                plan,
                budget,
                ResultMode,
                "None",
                runtimeForm,
                cacheKey,
                ResultArtifactHash)));

        return ValueTask.FromResult(new SandboxExecutionResult
        {
            Succeeded = Succeeded,
            Value = Succeeded ? ResultValue : null,
            Error = ResultError,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ResultMode,
            ModuleHash = ReturnWrongPlanIdentity ? "wrong-module" : plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash,
            ArtifactHash = ResultArtifactHash
        });
    }
}
