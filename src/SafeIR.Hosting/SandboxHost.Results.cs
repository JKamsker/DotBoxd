namespace SafeIR.Hosting;

using SafeIR;

public sealed partial class SandboxHost
{
    private static SandboxAuditEvent FallbackAudit(ExecutionPlan plan, SandboxRunId runId, SandboxError reason)
        => new(
            runId,
            "ExecutionFallback",
            DateTimeOffset.UtcNow,
            true,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: reason.Code,
            Message: $"compiled execution fell back to interpreted mode: {reason.SafeMessage}");

    private static SandboxExecutionResult CompilerUnavailableResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var error = new SandboxError(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CompilerUnavailable",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Compiled,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult CompiledFailureResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxError error)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CompiledExecutionFailed",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
        if (error.Code == SandboxErrorCode.VerifierFailure)
        {
            audit.Write(new SandboxAuditEvent(
                runId,
                "VerifierFailure",
                DateTimeOffset.UtcNow,
                false,
                ResourceId: $"module:{plan.ModuleHash}",
                ErrorCode: error.Code,
                Message: error.SafeMessage));
        }

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Compiled,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult DeterminismRequiredResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var error = new SandboxError(SandboxErrorCode.PolicyDenied, "deterministic execution is required");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "PolicyDenied",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = options.Mode,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult WorkerIsolationUnavailableResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var error = new SandboxError(
            SandboxErrorCode.PolicyDenied,
            "worker process isolation is not configured");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "WorkerIsolationUnavailable",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));

        return new SandboxExecutionResult
        {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = options.Mode,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }
}
